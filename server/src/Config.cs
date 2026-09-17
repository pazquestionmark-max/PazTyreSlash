using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using static CitizenFX.Core.Native.API;

namespace PazTyreSlashing.Server
{
    // Server-side view of config/config.json. Client-only keys (messages, minigame difficulties) are ignored here.
    [DataContract]
    public class Config
    {
        [DataMember(Name = "debug")] public bool Debug { get; set; }
        [DataMember(Name = "interaction")] public InteractionConfig Interaction { get; set; } = new InteractionConfig();
        [DataMember(Name = "weapons")] public List<WeaponConfig> Weapons { get; set; } = new List<WeaponConfig>();
        [DataMember(Name = "inventory")] public InventoryConfig Inventory { get; set; } = new InventoryConfig();
        [DataMember(Name = "durability")] public DurabilityConfig Durability { get; set; } = new DurabilityConfig();
        [DataMember(Name = "wheels")] public List<WheelConfig> Wheels { get; set; } = new List<WheelConfig>();
        [DataMember(Name = "puncture")] public PunctureConfig Puncture { get; set; } = new PunctureConfig();
        [DataMember(Name = "vehicles")] public VehicleConfig Vehicles { get; set; } = new VehicleConfig();
        [DataMember(Name = "minigame")] public MinigameConfig Minigame { get; set; } = new MinigameConfig();
        [DataMember(Name = "security")] public SecurityConfig Security { get; set; } = new SecurityConfig();
        [DataMember(Name = "breakage")] public BreakageConfig Breakage { get; set; } = new BreakageConfig();
        [DataMember(Name = "sounds")] public SoundsConfig Sounds { get; set; } = new SoundsConfig();

        // Lookups built once after load.
        public Dictionary<int, WeaponConfig> WeaponsByHash { get; private set; }
        public HashSet<int> WheelIndices { get; private set; }
        public HashSet<int> ExcludedModelHashes { get; private set; }

        public static Config Load()
        {
            var raw = LoadResourceFile(GetCurrentResourceName(), "config/config.json");
            if (string.IsNullOrEmpty(raw))
                throw new InvalidOperationException("config/config.json is missing or empty");

            Config config;
            var serializer = new DataContractJsonSerializer(typeof(Config));
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(raw)))
                config = (Config)serializer.ReadObject(stream);

            config.Validate();
            return config;
        }

        // Clamp nonsense values and build lookups so bad config fails loudly at startup, not mid-game.
        private void Validate()
        {
            if (Interaction == null || Weapons == null || Inventory == null || Durability == null || Wheels == null
                || Puncture == null || Vehicles == null || Minigame == null || Security == null || Breakage == null || Sounds == null)
                throw new InvalidOperationException("config.json is missing a required section");

            if (Interaction.ServerMaxDistance <= 0f) throw new InvalidOperationException("interaction.serverMaxDistance must be > 0");
            if (Minigame.Spots < 3 || Minigame.Spots > 5) throw new InvalidOperationException("minigame.spots must be between 3 and 5");
            if (Minigame.MinMsPerSpot < 0) Minigame.MinMsPerSpot = 0;
            if (Security.SessionTimeoutSeconds < Minigame.TimeLimitSeconds)
                throw new InvalidOperationException("security.sessionTimeoutSeconds must be >= minigame.timeLimitSeconds");
            if (Security.CooldownSeconds < 0) Security.CooldownSeconds = 0;
            if (Security.RateLimitMaxEvents < 1) Security.RateLimitMaxEvents = 1;
            if (Security.RateLimitWindowSeconds < 1) Security.RateLimitWindowSeconds = 1;
            if (Security.PunctureVerifyRetries < 1) Security.PunctureVerifyRetries = 1;
            if (Breakage.Chance < 0 || Breakage.Chance > 1) throw new InvalidOperationException("breakage.chance must be between 0 and 1");

            WeaponsByHash = new Dictionary<int, WeaponConfig>();
            foreach (var weapon in Weapons.Where(w => w.Enabled))
            {
                if (string.IsNullOrWhiteSpace(weapon.Weapon) || string.IsNullOrWhiteSpace(weapon.Item))
                    throw new InvalidOperationException("each weapons entry needs 'weapon' and 'item'");
                WeaponsByHash[GetHashKey(weapon.Weapon)] = weapon;
            }
            if (WeaponsByHash.Count == 0) throw new InvalidOperationException("no enabled weapons in config");

            WheelIndices = new HashSet<int>(Wheels.Select(w => w.Index));
            if (WheelIndices.Count == 0) throw new InvalidOperationException("no wheels configured");

            ExcludedModelHashes = new HashSet<int>((Vehicles.ExcludedModels ?? new List<string>()).Select(GetHashKey));
            Vehicles.ExcludedTypes = Vehicles.ExcludedTypes ?? new List<string>();
        }
    }

    [DataContract]
    public class InteractionConfig
    {
        [DataMember(Name = "serverMaxDistance")] public float ServerMaxDistance { get; set; } = 8f;
    }

    [DataContract]
    public class WeaponConfig
    {
        [DataMember(Name = "weapon")] public string Weapon { get; set; }
        [DataMember(Name = "item")] public string Item { get; set; }
        [DataMember(Name = "enabled")] public bool Enabled { get; set; }
    }

    [DataContract]
    public class InventoryConfig
    {
        [DataMember(Name = "requireItem")] public bool RequireItem { get; set; } = true;
        [DataMember(Name = "requireEquippedInOxInventory")] public bool RequireEquippedInOxInventory { get; set; } = true;
    }

    [DataContract]
    public class DurabilityConfig
    {
        [DataMember(Name = "enabled")] public bool Enabled { get; set; }
        [DataMember(Name = "amount")] public double Amount { get; set; } = 2.0;
    }

    [DataContract]
    public class WheelConfig
    {
        [DataMember(Name = "index")] public int Index { get; set; }
        [DataMember(Name = "bone")] public string Bone { get; set; }
    }

    [DataContract]
    public class PunctureConfig
    {
        [DataMember(Name = "onRim")] public bool OnRim { get; set; }
    }

    [DataContract]
    public class VehicleConfig
    {
        [DataMember(Name = "excludedModels")] public List<string> ExcludedModels { get; set; } = new List<string>();
        [DataMember(Name = "excludedTypes")] public List<string> ExcludedTypes { get; set; } = new List<string>();
    }

    [DataContract]
    public class MinigameConfig
    {
        [DataMember(Name = "spots")] public int Spots { get; set; } = 4;
        [DataMember(Name = "timeLimitSeconds")] public int TimeLimitSeconds { get; set; } = 10;
        [DataMember(Name = "minMsPerSpot")] public int MinMsPerSpot { get; set; } = 250;
    }

    [DataContract]
    public class SecurityConfig
    {
        [DataMember(Name = "cooldownSeconds")] public int CooldownSeconds { get; set; } = 10;
        [DataMember(Name = "sessionTimeoutSeconds")] public int SessionTimeoutSeconds { get; set; } = 25;
        [DataMember(Name = "rateLimitWindowSeconds")] public int RateLimitWindowSeconds { get; set; } = 5;
        [DataMember(Name = "rateLimitMaxEvents")] public int RateLimitMaxEvents { get; set; } = 6;
        [DataMember(Name = "punctureVerifyRetries")] public int PunctureVerifyRetries { get; set; } = 3;
    }

    [DataContract]
    public class BreakageConfig
    {
        [DataMember(Name = "enabled")] public bool Enabled { get; set; }
        [DataMember(Name = "chance")] public double Chance { get; set; } = 0.02;
    }

    [DataContract]
    public class SoundsConfig
    {
        [DataMember(Name = "enabled")] public bool Enabled { get; set; } = true;
        [DataMember(Name = "stabRange")] public float StabRange { get; set; } = 15f;
        [DataMember(Name = "punctureRange")] public float PunctureRange { get; set; } = 35f;
    }
}
