using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace PazTyreSlashing.Server
{
    public enum SessionState
    {
        Validating,
        InProgress,
        Completed,
        Cancelled,
        Failed,
        Expired
    }

    public class SlashSession
    {
        public string Id { get; set; }
        public int PlayerId { get; set; }
        public int VehicleNetId { get; set; }
        public int TyreIndex { get; set; }
        public int WeaponHash { get; set; }
        public long StartedAt { get; set; }
        public int StabCount { get; set; }
        public SessionState State { get; set; }

        public string TyreKey => SessionManager.TyreKey(VehicleNetId, TyreIndex);
        public bool IsTerminal => State >= SessionState.Completed;
    }

    // Owns all per-player mutable state: sessions, tyre locks, cooldowns and rate limits.
    // FiveM C# scripts run on a single thread, so no locking is needed.
    public class SessionManager
    {
        private readonly Config _config;
        private readonly Dictionary<int, SlashSession> _sessionsByPlayer = new Dictionary<int, SlashSession>();
        private readonly Dictionary<string, int> _tyreLocks = new Dictionary<string, int>();
        private readonly Dictionary<int, long> _cooldownUntil = new Dictionary<int, long>();
        private readonly Dictionary<int, Queue<long>> _eventTimes = new Dictionary<int, Queue<long>>();

        public SessionManager(Config config)
        {
            _config = config;
        }

        public static string TyreKey(int netId, int tyreIndex) => netId + ":" + tyreIndex;

        private static readonly Stopwatch Clock = Stopwatch.StartNew();

        // Monotonic milliseconds; immune to wall-clock changes and TickCount wrap.
        public static long Now => Clock.ElapsedMilliseconds;

        // Sliding-window limiter shared by every client event this resource accepts.
        public bool IsRateLimited(int playerId)
        {
            var now = Now;
            if (!_eventTimes.TryGetValue(playerId, out var times))
                _eventTimes[playerId] = times = new Queue<long>();

            var windowMs = _config.Security.RateLimitWindowSeconds * 1000L;
            while (times.Count > 0 && now - times.Peek() > windowMs)
                times.Dequeue();

            if (times.Count >= _config.Security.RateLimitMaxEvents)
                return true;

            times.Enqueue(now);
            return false;
        }

        public bool IsOnCooldown(int playerId) =>
            _cooldownUntil.TryGetValue(playerId, out var until) && Now < until;

        public void StartCooldown(int playerId) =>
            _cooldownUntil[playerId] = Now + _config.Security.CooldownSeconds * 1000L;

        public SlashSession GetActive(int playerId) =>
            _sessionsByPlayer.TryGetValue(playerId, out var session) && !session.IsTerminal ? session : null;

        public bool IsTyreLocked(int netId, int tyreIndex) => _tyreLocks.ContainsKey(TyreKey(netId, tyreIndex));

        public SlashSession Create(int playerId, int netId, int tyreIndex, int weaponHash)
        {
            var session = new SlashSession
            {
                Id = Guid.NewGuid().ToString("N"),
                PlayerId = playerId,
                VehicleNetId = netId,
                TyreIndex = tyreIndex,
                WeaponHash = weaponHash,
                StartedAt = Now,
                State = SessionState.Validating
            };

            _sessionsByPlayer[playerId] = session;
            _tyreLocks[session.TyreKey] = playerId;
            return session;
        }

        // Only the initiating player with the exact session id may act on a live session.
        public SlashSession Resolve(int playerId, string sessionId)
        {
            var session = GetActive(playerId);
            if (session == null || string.IsNullOrEmpty(sessionId) || session.Id != sessionId)
                return null;
            return session;
        }

        // Moves a session to a terminal state exactly once and releases its tyre lock.
        public bool End(SlashSession session, SessionState terminalState)
        {
            if (session.IsTerminal || terminalState < SessionState.Completed)
                return false;

            session.State = terminalState;
            _sessionsByPlayer.Remove(session.PlayerId);
            if (_tyreLocks.TryGetValue(session.TyreKey, out var owner) && owner == session.PlayerId)
                _tyreLocks.Remove(session.TyreKey);
            StartCooldown(session.PlayerId);
            return true;
        }

        public IEnumerable<SlashSession> Expired()
        {
            var timeoutMs = _config.Security.SessionTimeoutSeconds * 1000L;
            var now = Now;
            return _sessionsByPlayer.Values.Where(s => now - s.StartedAt > timeoutMs).ToList();
        }

        public IEnumerable<SlashSession> ForVehicle(int netId) =>
            _sessionsByPlayer.Values.Where(s => s.VehicleNetId == netId).ToList();

        public void ForgetPlayer(int playerId)
        {
            if (_sessionsByPlayer.TryGetValue(playerId, out var session))
                End(session, SessionState.Cancelled);
            _cooldownUntil.Remove(playerId);
            _eventTimes.Remove(playerId);
        }
    }
}
