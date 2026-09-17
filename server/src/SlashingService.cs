using System;
using System.Threading.Tasks;
using CitizenFX.Core;
using static CitizenFX.Core.Native.API;

namespace PazTyreSlashing.Server
{
    public static class Events
    {
        // Client -> server (untrusted).
        public const string ServerRequest = "paz_tyre_slashing:server:request";
        public const string ServerFinish = "paz_tyre_slashing:server:finish";
        public const string ServerCancel = "paz_tyre_slashing:server:cancel";
        public const string ServerStab = "paz_tyre_slashing:server:stab";

        // Server -> client.
        public const string ClientStart = "paz_tyre_slashing:client:start";
        public const string ClientAbort = "paz_tyre_slashing:client:abort";
        public const string ClientApplyPuncture = "paz_tyre_slashing:client:applyPuncture";
        public const string ClientNotify = "paz_tyre_slashing:client:notify";
        public const string ClientSound = "paz_tyre_slashing:client:sound";
    }

    // Orchestrates the session state machine: Validating -> InProgress -> Completed | Cancelled | Failed | Expired.
    public class SlashingService
    {
        private readonly Config _config;
        private readonly PlayerList _players;
        private readonly SessionManager _sessions;
        private readonly Validator _validator;
        private readonly TyreService _tyres;
        private readonly Random _random = new Random();

        public SlashingService(Config config, PlayerList players, SessionManager sessions, Validator validator, TyreService tyres)
        {
            _config = config;
            _players = players;
            _sessions = sessions;
            _validator = validator;
            _tyres = tyres;
        }

        public void HandleRequest(int playerId, int netId, int tyreIndex)
        {
            if (_sessions.GetActive(playerId) != null) { Notify(playerId, "busy", "error"); return; }
            if (_sessions.IsOnCooldown(playerId)) { Notify(playerId, "cooldown", "error"); return; }
            if (_sessions.IsTyreLocked(netId, tyreIndex)) { Notify(playerId, "tyre_busy", "error"); return; }

            var playerCheck = _validator.ValidatePlayer(playerId);
            if (!playerCheck.Ok) { Notify(playerId, playerCheck.Error, "error"); return; }

            var weaponCheck = _validator.ValidateWeapon(playerId);
            if (!weaponCheck.Ok) { Notify(playerId, weaponCheck.Error, "error"); return; }

            var vehicleCheck = _validator.ValidateVehicle(playerId, netId, tyreIndex);
            if (!vehicleCheck.Ok) { Notify(playerId, vehicleCheck.Error, "error"); return; }

            var session = _sessions.Create(playerId, netId, tyreIndex, weaponCheck.WeaponHash);
            session.State = SessionState.InProgress;
            Log($"session {session.Id} started by {playerId} on {session.TyreKey}");

            _players[playerId].TriggerEvent(Events.ClientStart, session.Id, netId, tyreIndex);
        }

        public async Task HandleFinish(int playerId, string sessionId, bool minigameSucceeded)
        {
            var session = _sessions.Resolve(playerId, sessionId);
            if (session == null)
            {
                // Forged, replayed or stale completion: ignore silently apart from a debug log.
                Log($"rejected finish from {playerId}: no matching live session");
                return;
            }

            if (!minigameSucceeded)
            {
                End(session, SessionState.Failed, "minigame_failed");
                return;
            }

            // A real minigame cannot be completed faster than this; instant completions are forged.
            var elapsed = SessionManager.Now - session.StartedAt;
            var minimumMs = _config.Minigame.Spots * _config.Minigame.MinMsPerSpot;
            if (elapsed < minimumMs)
            {
                Log($"rejected finish from {playerId}: completed in {elapsed}ms (< {minimumMs}ms)");
                End(session, SessionState.Failed, "minigame_failed");
                return;
            }

            // Re-validate everything: state may have changed during the minigame.
            var playerCheck = _validator.ValidatePlayer(playerId);
            var weaponCheck = playerCheck.Ok ? _validator.ValidateWeapon(playerId, session.WeaponHash) : playerCheck;
            var vehicleCheck = weaponCheck.Ok ? _validator.ValidateVehicle(playerId, session.VehicleNetId, session.TyreIndex) : weaponCheck;
            if (!vehicleCheck.Ok)
            {
                End(session, SessionState.Failed, vehicleCheck.Error);
                return;
            }

            // Mark completed before the async puncture so a replayed finish cannot start a second one.
            _sessions.End(session, SessionState.Completed);
            Log($"session {session.Id} completed; applying puncture to {session.TyreKey}");

            var ped = GetPlayerPed(playerId.ToString());
            var weaponSnapped = false;
            if (_config.Breakage.Enabled)
            {
                var roll = _random.NextDouble();
                weaponSnapped = roll < _config.Breakage.Chance
                    && _tyres.BreakWeapon(playerId, weaponCheck.InventoryWeapon, _config.WeaponsByHash[session.WeaponHash].Item);
                Log($"blade break roll {roll:0.000} vs chance {_config.Breakage.Chance:0.000}: {(weaponSnapped ? "snapped" : "intact")}");
            }
            if (weaponSnapped)
            {
                Notify(playerId, "weapon_snapped", "error");
                _tyres.BroadcastSound("snap", GetEntityCoords(ped), _config.Sounds.StabRange, GetPlayerRoutingBucket(playerId.ToString()));
            }
            else
            {
                _tyres.ConsumeDurability(playerId, weaponCheck.InventoryWeapon);
            }

            var punctured = await _tyres.Puncture(session.VehicleNetId, session.TyreIndex);
            Notify(playerId, punctured ? "punctured" : "server_error", punctured ? "success" : "error");
            if (!punctured)
                Debug.WriteLine($"^3[paz_tyre_slashing] puncture on {session.TyreKey} could not be confirmed^0");
        }

        // Relays one minigame stab sound to nearby players. Bounded per session instead of by the rate limiter,
        // so a fast minigame cannot starve the finish event.
        public void HandleStab(int playerId, string sessionId)
        {
            var session = _sessions.Resolve(playerId, sessionId);
            if (session == null || session.StabCount >= _config.Minigame.Spots)
                return;

            session.StabCount++;
            var playerIdString = playerId.ToString();
            _tyres.BroadcastSound("stab", GetEntityCoords(GetPlayerPed(playerIdString)), _config.Sounds.StabRange,
                GetPlayerRoutingBucket(playerIdString), excludePlayerId: playerId);
        }

        public void HandleCancel(int playerId, string sessionId)
        {
            var session = _sessions.Resolve(playerId, sessionId);
            // The client already told the player it cancelled, so no message is sent back.
            if (session != null)
                End(session, SessionState.Cancelled, null);
        }

        public void ExpireSessions()
        {
            foreach (var session in _sessions.Expired())
                End(session, SessionState.Expired, "expired");
        }

        public void HandleEntityRemoved(int entity)
        {
            if (GetEntityType(entity) != 2)
                return;

            var netId = NetworkGetNetworkIdFromEntity(entity);
            foreach (var session in _sessions.ForVehicle(netId))
                End(session, SessionState.Cancelled, "invalid_target");
        }

        public void HandlePlayerDropped(int playerId)
        {
            _sessions.ForgetPlayer(playerId);
        }

        private void End(SlashSession session, SessionState state, string messageKey)
        {
            if (!_sessions.End(session, state))
                return;

            Log($"session {session.Id} ended: {state}");
            _players[session.PlayerId]?.TriggerEvent(Events.ClientAbort, session.Id, messageKey);
        }

        private void Notify(int playerId, string messageKey, string type)
        {
            _players[playerId]?.TriggerEvent(Events.ClientNotify, messageKey, type);
        }

        private void Log(string message)
        {
            if (_config.Debug)
                Debug.WriteLine($"[paz_tyre_slashing] {message}");
        }
    }
}
