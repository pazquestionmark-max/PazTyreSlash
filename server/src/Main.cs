using System;
using System.Threading.Tasks;
using CitizenFX.Core;

namespace PazTyreSlashing.Server
{
    // Entry point: loads config, wires net events and runs the session expiry tick.
    public class Main : BaseScript
    {
        private readonly SlashingService _service;
        private readonly SessionManager _sessions;

        public Main()
        {
            Config config;
            try
            {
                config = Config.Load();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"^1[paz_tyre_slashing] invalid configuration, resource disabled: {ex.Message}^0");
                return;
            }

            _sessions = new SessionManager(config);
            var validator = new Validator(config, Players, Exports);
            var tyres = new TyreService(config, Players, Exports);
            _service = new SlashingService(config, Players, _sessions, validator, tyres);

            EventHandlers[Events.ServerRequest] += new Action<Player, object, object>(OnRequest);
            EventHandlers[Events.ServerFinish] += new Action<Player, object, object>(OnFinish);
            EventHandlers[Events.ServerCancel] += new Action<Player, object>(OnCancel);
            EventHandlers[Events.ServerStab] += new Action<Player, object>(OnStab);
            EventHandlers["playerDropped"] += new Action<Player, string>(OnPlayerDropped);
            EventHandlers["entityRemoved"] += new Action<int>(entity => _service.HandleEntityRemoved(entity));

            Tick += ExpiryTick;
        }

        // Arguments are typed as object: forged payloads with wrong types must be rejected, not throw.
        private void OnRequest([FromSource] Player source, object netId, object tyreIndex)
        {
            var playerId = SourceId(source);
            if (playerId <= 0 || _sessions.IsRateLimited(playerId)) return;
            if (!TryInt(netId, out var net) || !TryInt(tyreIndex, out var tyre)) return;

            _service.HandleRequest(playerId, net, tyre);
        }

        private async void OnFinish([FromSource] Player source, object sessionId, object success)
        {
            var playerId = SourceId(source);
            if (playerId <= 0 || _sessions.IsRateLimited(playerId)) return;
            if (!(sessionId is string id) || !(success is bool succeeded)) return;

            try
            {
                await _service.HandleFinish(playerId, id, succeeded);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"^1[paz_tyre_slashing] finish failed: {ex}^0");
            }
        }

        private void OnCancel([FromSource] Player source, object sessionId)
        {
            var playerId = SourceId(source);
            if (playerId <= 0 || _sessions.IsRateLimited(playerId)) return;
            if (sessionId is string id) _service.HandleCancel(playerId, id);
        }

        private void OnStab([FromSource] Player source, object sessionId)
        {
            var playerId = SourceId(source);
            if (playerId > 0 && sessionId is string id) _service.HandleStab(playerId, id);
        }

        private void OnPlayerDropped([FromSource] Player source, string reason)
        {
            var playerId = SourceId(source);
            if (playerId > 0) _service.HandlePlayerDropped(playerId);
        }

        private async Task ExpiryTick()
        {
            _service.ExpireSessions();
            await Delay(1000);
        }

        private static int SourceId(Player source) =>
            source != null && int.TryParse(source.Handle, out var id) ? id : 0;

        private static bool TryInt(object value, out int result)
        {
            result = 0;
            switch (value)
            {
                case int i: result = i; return true;
                case long l when l >= int.MinValue && l <= int.MaxValue: result = (int)l; return true;
                case uint u when u <= int.MaxValue: result = (int)u; return true;
                case ushort s: result = s; return true;
                case byte b: result = b; return true;
                case sbyte sb: result = sb; return true;
                case short sh: result = sh; return true;
                case ulong ul when ul <= int.MaxValue: result = (int)ul; return true;
                default: return false;
            }
        }
    }
}
