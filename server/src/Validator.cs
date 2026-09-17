using System;
using System.Collections.Generic;
using CitizenFX.Core;
using static CitizenFX.Core.Native.API;

namespace PazTyreSlashing.Server
{
    // Result of a validation step: an error message key (from config messages) or null when valid.
    public class Check
    {
        public string Error { get; private set; }
        public int WeaponHash { get; private set; }
        public int Vehicle { get; private set; }
        public IDictionary<string, object> InventoryWeapon { get; private set; }

        public bool Ok => Error == null;

        public static Check Fail(string error) => new Check { Error = error };
        public static Check Pass(int weaponHash = 0, int vehicle = 0, IDictionary<string, object> inventoryWeapon = null) =>
            new Check { WeaponHash = weaponHash, Vehicle = vehicle, InventoryWeapon = inventoryWeapon };
    }

    // All authoritative checks live here so request and completion share exactly the same rules.
    public class Validator
    {
        private const int EntityTypeVehicle = 2;
        private const int DeadHealthThreshold = 100;

        private readonly Config _config;
        private readonly PlayerList _players;
        private readonly ExportDictionary _exports;

        public Validator(Config config, PlayerList players, ExportDictionary exports)
        {
            _config = config;
            _players = players;
            _exports = exports;
        }

        public Check ValidatePlayer(int playerId)
        {
            var player = _players[playerId];
            if (player == null || string.IsNullOrEmpty(player.Name))
                return Check.Fail("invalid_target");

            var ped = GetPlayerPed(playerId.ToString());
            if (ped == 0 || !DoesEntityExist(ped))
                return Check.Fail("invalid_target");

            // qbx_core sets isLoggedIn; qbx_medical sets isDead (covers last stand too).
            if (!IsTrue(player.State["isLoggedIn"]) || IsTrue(player.State["isDead"]))
                return Check.Fail("incapacitated");
            if (GetEntityHealth(ped) <= DeadHealthThreshold)
                return Check.Fail("incapacitated");
            if (GetVehiclePedIsIn(ped, false) != 0)
                return Check.Fail("incapacitated");

            return Check.Pass();
        }

        // Weapon state comes from the ped sync tree and ox_inventory's server-side record of the equipped slot.
        // Both originate from the client, so a modified client can still lie; see README "Known limitations".
        public Check ValidateWeapon(int playerId, int expectedWeaponHash = 0)
        {
            var ped = GetPlayerPed(playerId.ToString());
            var weaponHash = unchecked((int)GetSelectedPedWeapon(ped));

            if (!_config.WeaponsByHash.TryGetValue(weaponHash, out var weapon))
                return Check.Fail("weapon_not_equipped");
            if (expectedWeaponHash != 0 && weaponHash != expectedWeaponHash)
                return Check.Fail("weapon_not_equipped");

            IDictionary<string, object> inventoryWeapon = null;
            try
            {
                if (_config.Inventory.RequireItem)
                {
                    int count = Convert.ToInt32(_exports["ox_inventory"].GetItemCount(playerId, weapon.Item) ?? 0);
                    if (count < 1)
                        return Check.Fail("missing_item");
                }

                if (_config.Inventory.RequireEquippedInOxInventory || _config.Durability.Enabled || _config.Breakage.Enabled)
                {
                    inventoryWeapon = _exports["ox_inventory"].GetCurrentWeapon(playerId) as IDictionary<string, object>;
                    var equippedName = inventoryWeapon != null && inventoryWeapon.TryGetValue("name", out var name) ? name as string : null;

                    if (_config.Inventory.RequireEquippedInOxInventory
                        && !string.Equals(equippedName, weapon.Item, StringComparison.OrdinalIgnoreCase))
                        return Check.Fail("weapon_not_equipped");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"^1[paz_tyre_slashing] ox_inventory export failed: {ex.Message}^0");
                return Check.Fail("server_error");
            }

            return Check.Pass(weaponHash: weaponHash, inventoryWeapon: inventoryWeapon);
        }

        public Check ValidateVehicle(int playerId, int netId, int tyreIndex)
        {
            if (netId <= 0 || !_config.WheelIndices.Contains(tyreIndex))
                return Check.Fail("invalid_target");

            var vehicle = NetworkGetEntityFromNetworkId(netId);
            if (vehicle == 0 || !DoesEntityExist(vehicle) || GetEntityType(vehicle) != EntityTypeVehicle)
                return Check.Fail("invalid_target");

            var type = GetVehicleType(vehicle);
            if (type == null || _config.Vehicles.ExcludedTypes.Contains(type))
                return Check.Fail("invalid_target");
            if (_config.ExcludedModelHashes.Contains(unchecked((int)GetEntityModel(vehicle))))
                return Check.Fail("invalid_target");

            var playerIdString = playerId.ToString();
            if (GetEntityRoutingBucket(vehicle) != GetPlayerRoutingBucket(playerIdString))
                return Check.Fail("invalid_target");

            // Wheel bone positions are not available on the server; compare against the vehicle origin
            // with a tolerance that covers long vehicles (interaction.serverMaxDistance).
            var ped = GetPlayerPed(playerIdString);
            var distance = Vector3.Distance(GetEntityCoords(ped), GetEntityCoords(vehicle));
            if (distance > _config.Interaction.ServerMaxDistance)
                return Check.Fail("too_far");

            // No server-side "already punctured" check here: the server IS_VEHICLE_TYRE_BURST indexes the
            // synced wheel slot (0..wheelCount-1), not the native tyre id, so rear tyres would be misread.
            // The client checks it, and TyreService only confirms a puncture if a new tyre actually burst.

            return Check.Pass(vehicle: vehicle);
        }

        private static bool IsTrue(object value) => value is bool b && b;
    }
}
