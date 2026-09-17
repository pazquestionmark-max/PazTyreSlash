using System;
using PazTyreSlashing.Server;

// Config.Validate/Load call FiveM natives, so build the config by hand; SessionManager itself is native-free.
var config = new Config
{
    Security = new SecurityConfig { CooldownSeconds = 60, SessionTimeoutSeconds = 0, RateLimitWindowSeconds = 60, RateLimitMaxEvents = 3 }
};
var sessions = new SessionManager(config);

void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAILED: " + name);
    Console.WriteLine("ok   " + name);
}

// Rate limiting
Check(!sessions.IsRateLimited(1) && !sessions.IsRateLimited(1) && !sessions.IsRateLimited(1), "first events allowed");
Check(sessions.IsRateLimited(1), "event over limit rejected");
Check(!sessions.IsRateLimited(2), "limit is per player");

// Session ownership and nonce
var session = sessions.Create(10, 500, 0, 123);
session.State = SessionState.InProgress;
Check(sessions.GetActive(10) == session, "active session tracked");
Check(sessions.IsTyreLocked(500, 0), "tyre locked while in progress");
Check(!sessions.IsTyreLocked(500, 1), "other tyres on same vehicle stay free");
Check(sessions.Resolve(10, "forged") == null, "forged session id rejected");
Check(sessions.Resolve(11, session.Id) == null, "other player cannot use session id");
Check(sessions.Resolve(10, session.Id) == session, "owner resolves own session");

// Terminal transition happens once and releases locks
Check(sessions.End(session, SessionState.Completed), "session completes");
Check(!sessions.End(session, SessionState.Completed), "double completion rejected");
Check(sessions.Resolve(10, session.Id) == null, "replayed completion rejected");
Check(!sessions.IsTyreLocked(500, 0), "tyre lock released");
Check(sessions.IsOnCooldown(10), "cooldown started after session");
Check(!sessions.End(sessions.Create(12, 1, 0, 0), SessionState.InProgress), "non-terminal End rejected");

// Expiry and disconnect cleanup (timeout 0 => everything is expired)
var stale = sessions.Create(20, 700, 4, 1);
System.Threading.Thread.Sleep(5);
Check(sessions.Expired().GetEnumerator().MoveNext(), "stale session reported as expired");
sessions.ForgetPlayer(20);
Check(sessions.GetActive(20) == null && !sessions.IsTyreLocked(700, 4), "disconnect clears session and lock");
Check(stale.State == SessionState.Cancelled, "disconnect marks session cancelled");

Console.WriteLine("all session checks passed");
