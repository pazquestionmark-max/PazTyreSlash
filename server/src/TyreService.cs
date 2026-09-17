using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CitizenFX.Core;
using static CitizenFX.Core.Native.API;

namespace PazTyreSlashing.Server
{
    // Applies server-approved tyre damage. SET_VEHICLE_TYRE_BURST is client-only, so the current network
    // owner applies it and the server confirms the result through the synced IS_VEHICLE_TYRE_BURST state.
    public class TyreService
    {
        private const int VerifyDelayMs = 750;
        private const int MaxSyncedTyreSlots = 16;

        private readonly Config _config;
        private readonly PlayerList _players;
        private readonly ExportDictionary _exports;

        public TyreService(Config config, PlayerList players, ExportDictionary exports)
        {
            _config = config;
            _players = players;
            _exports = exports;
        }

        // The server IS_VEHICLE_TYRE_BURST reads synced wheel *slots* (0..wheelCount-1), which do not match
        // native tyre ids (a car's rear tyres are ids 4/5 but slots 2/3). Counting burst slots before and after
        // confirms the puncture without relying on that mapping.
        public async Task<bool> Puncture(int netId, int tyreIndex)
        {
            var vehicle = NetworkGetEntityFromNetworkId(netId);
            if (vehicle == 0 || !DoesEntityExist(vehicle))
                return false;

            var burstBefore = CountBurstTyres(vehicle);

            for (var attempt = 0; attempt < _config.Security.PunctureVerifyRetries; attempt++)
            {
                vehicle = NetworkGetEntityFromNetworkId(netId);
                if (vehicle == 0 || !DoesEntityExist(vehicle))
                    return false;

                // Re-read the owner every attempt so migration between attempts is handled.
                // Never target id -1: TriggerClientEvent(-1) broadcasts to every player.
                var ownerId = NetworkGetEntityOwner(vehicle);
                var owner = ownerId > 0 ? _players[ownerId] : null;
                if (owner != null)
                    owner.TriggerEvent(Events.ClientApplyPuncture, netId, tyreIndex, _config.Puncture.OnRim);

                await BaseScript.Delay(VerifyDelayMs);

                vehicle = NetworkGetEntityFromNetworkId(netId);
                if (vehicle != 0 && DoesEntityExist(vehicle) && CountBurstTyres(vehicle) > burstBefore)
                {
                    BroadcastSound("puncture", GetEntityCoords(vehicle), _config.Sounds.PunctureRange, GetEntityRoutingBucket(vehicle));
                    return true;
                }
            }

            return false;
        }

        private static int CountBurstTyres(int vehicle)
        {
            var count = 0;
            for (var slot = 0; slot < MaxSyncedTyreSlots; slot++)
                if (IsVehicleTyreBurst(vehicle, slot, false) || IsVehicleTyreBurst(vehicle, slot, true))
                    count++;
            return count;
        }

        // Removes the equipped blade from the player's ox_inventory. ox_inventory disarms the client itself.
        public bool BreakWeapon(int playerId, IDictionary<string, object> inventoryWeapon, string itemName)
        {
            try
            {
                object slot = null;
                inventoryWeapon?.TryGetValue("slot", out slot);
                object result = slot != null
                    ? _exports["ox_inventory"].RemoveItem(playerId, itemName, 1, null, Convert.ToInt32(slot))
                    : _exports["ox_inventory"].RemoveItem(playerId, itemName, 1);

                // RemoveItem returns (success, reason); multiple Lua returns arrive as a list.
                if (result is IList<object> values)
                    result = values.Count > 0 ? values[0] : null;
                return result is bool removed && removed;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"^3[paz_tyre_slashing] weapon break skipped: {ex.Message}^0");
                return false;
            }
        }

        // Sends a positional sound to every player in range; each client scales volume by its own distance.
        public void BroadcastSound(string sound, Vector3 origin, float range, int routingBucket, int excludePlayerId = 0)
        {
            if (!_config.Sounds.Enabled)
                return;

            foreach (var player in _players)
            {
                if (!int.TryParse(player.Handle, out var id) || id == excludePlayerId)
                    continue;
                if (GetPlayerRoutingBucket(player.Handle) != routingBucket)
                    continue;

                var ped = GetPlayerPed(player.Handle);
                if (ped == 0 || Vector3.Distance(GetEntityCoords(ped), origin) > range)
                    continue;

                player.TriggerEvent(Events.ClientSound, sound, origin.X, origin.Y, origin.Z, range);
            }
        }

        public void ConsumeDurability(int playerId, IDictionary<string, object> inventoryWeapon)
        {
            if (!_config.Durability.Enabled || inventoryWeapon == null)
                return;

            try
            {
                if (!inventoryWeapon.TryGetValue("slot", out var slot)
                    || !inventoryWeapon.TryGetValue("metadata", out var metadataObject)
                    || !(metadataObject is IDictionary<string, object> metadata)
                    || !metadata.TryGetValue("durability", out var durability))
                    return;

                var next = Math.Max(0.0, Convert.ToDouble(durability) - _config.Durability.Amount);
                _exports["ox_inventory"].SetDurability(playerId, Convert.ToInt32(slot), next);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"^3[paz_tyre_slashing] durability update skipped: {ex.Message}^0");
            }
        }
    }
}
