using System;
using System.Collections.Generic;

namespace Hullbreach.Platform
{
    // Typed calls to the platform auth API; turns every HTTP answer or transport
    // failure into an outcome so callers never see exceptions or raw status codes.
    // frob:doc docs/platform-link.md#hullbreachplatform-module-reference
    public sealed class PlatformClient
    {
        // frob:doc docs/platform-link.md#hullbreachplatform-module-reference
        public const string LoginPath = "/api/v1/auth/login";

        // frob:doc docs/platform-link.md#hullbreachplatform-module-reference
        public const string SessionPath = "/api/v1/auth/session";

        // frob:doc docs/platform-link.md#remembered-session
        public const string ProfilePath = "/api/v1/me";

        // frob:doc docs/platform-link.md#sign-in-screen
        public const string LogoutPath = "/api/v1/auth/logout";

        // frob:doc docs/platform-link.md#reporting-a-finished-match
        public const string MatchesPath = "/api/v1/matches";

        // frob:doc docs/platform-link.md#hullbreachplatform-module-reference
        public const string ServerKeyHeader = "X-Server-Key";

        readonly PlatformConfig _config;
        readonly IPlatformHttp _http;
        readonly IPlatformLog _log;

        // frob:doc docs/platform-link.md#hullbreachplatform-module-reference
        public PlatformClient(PlatformConfig config, IPlatformHttp http, IPlatformLog log = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _log = log ?? NullPlatformLog.Instance;
        }

        // frob:doc docs/platform-link.md#hullbreachplatform-module-reference
        public PlatformConfig Config => _config;

        // Player sign-in. The password goes only into the request body; logs carry the username.
        // frob:doc docs/platform-link.md#hullbreachplatform-module-reference
        public void Login(string username, string password, Action<LoginOutcome> onComplete)
        {
            if (onComplete == null) throw new ArgumentNullException(nameof(onComplete));
            username = username?.Trim() ?? string.Empty;
            password = password ?? string.Empty;
            if (username.Length == 0 || password.Length == 0)
            {
                onComplete(new LoginOutcome(LoginStatus.InvalidCredentials, null, username,
                    "Enter a username and password."));
                return;
            }

            string body = "{\"username\":" + PlatformJson.Quote(username) +
                          ",\"password\":" + PlatformJson.Quote(password) + "}";
            var request = new PlatformHttpRequest(PlatformHttpMethod.Post, _config.BaseUrl + LoginPath, body,
                null, _config.TimeoutSeconds);
            _log.Info($"Hullbreach platform: login request for '{username}' to {_config.BaseUrl}.");
            _http.Send(request, response =>
            {
                LoginOutcome outcome = MapLogin(username, response);
                if (outcome.IsSuccess)
                    _log.Info($"Hullbreach platform: login succeeded for '{outcome.Username}'.");
                else
                    _log.Warning($"Hullbreach platform: login failed for '{username}': {outcome.Status} ({outcome.Message}).");
                onComplete(outcome);
            });
        }

        // Game-server check of a player's token. Sends X-Server-Key when configured so the
        // call keeps working once the platform starts requiring it for server routes.
        // frob:doc docs/platform-link.md#hullbreachplatform-module-reference
        public void ValidateSession(string token, Action<SessionOutcome> onComplete)
        {
            if (onComplete == null) throw new ArgumentNullException(nameof(onComplete));
            if (string.IsNullOrEmpty(token))
            {
                onComplete(new SessionOutcome(SessionStatus.Invalid, null, null, "no platform token presented"));
                return;
            }

            var headers = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("Authorization", "Bearer " + token),
            };
            if (_config.HasServerKey)
                headers.Add(new KeyValuePair<string, string>(ServerKeyHeader, _config.ServerKey));

            var request = new PlatformHttpRequest(PlatformHttpMethod.Get, _config.BaseUrl + SessionPath, null,
                headers, _config.TimeoutSeconds);
            _http.Send(request, response =>
            {
                SessionOutcome outcome = MapSession(response);
                if (outcome.Status == SessionStatus.Unreachable || outcome.Status == SessionStatus.Failed)
                    _log.Warning($"Hullbreach platform: session check {outcome.Status} ({outcome.Message}).");
                onComplete(outcome);
            });
        }

        // clients check their own profile; only game servers use the server-key endpoint.
        // frob:doc docs/platform-link.md#remembered-session
        public void RestoreSession(string token, Action<LoginOutcome> onComplete)
        {
            if (onComplete == null) throw new ArgumentNullException(nameof(onComplete));
            if (string.IsNullOrEmpty(token))
            {
                onComplete(new LoginOutcome(LoginStatus.InvalidCredentials, null, null,
                    "Your saved session expired. Sign in again."));
                return;
            }

            var headers = new[] { new KeyValuePair<string, string>("Authorization", "Bearer " + token) };
            var request = new PlatformHttpRequest(PlatformHttpMethod.Get, _config.BaseUrl + ProfilePath,
                null, headers, _config.TimeoutSeconds);
            _http.Send(request, response =>
            {
                if (response.IsTransportFailure)
                {
                    onComplete(new LoginOutcome(LoginStatus.Unreachable, null, null,
                        "Cannot restore your saved session right now. Sign in or play offline."));
                    return;
                }
                if (response.StatusCode == 200 && PlatformJson.TryParseObject(response.Body, out var profile))
                {
                    string userId = PlatformJson.GetString(profile, "id");
                    string username = PlatformJson.GetString(profile, "username");
                    if (!string.IsNullOrEmpty(userId) && !string.IsNullOrEmpty(username))
                    {
                        onComplete(new LoginOutcome(LoginStatus.Success, token, username, "Session restored."));
                        return;
                    }
                }
                bool expired = response.StatusCode == 401;
                onComplete(new LoginOutcome(expired ? LoginStatus.InvalidCredentials : LoginStatus.Failed,
                    null, null, expired ? "Your saved session expired. Sign in again."
                        : "Cannot restore your saved session right now. Sign in or play offline."));
            });
        }

        // revoke only this session; local sign-out is handled by the screen model.
        // frob:doc docs/platform-link.md#sign-in-screen
        public void Logout(string token, Action<bool> onComplete)
        {
            if (onComplete == null) throw new ArgumentNullException(nameof(onComplete));
            if (string.IsNullOrEmpty(token))
            {
                onComplete(true);
                return;
            }

            var headers = new[] { new KeyValuePair<string, string>("Authorization", "Bearer " + token) };
            var request = new PlatformHttpRequest(PlatformHttpMethod.Post, _config.BaseUrl + LogoutPath,
                null, headers, _config.TimeoutSeconds);
            _http.Send(request, response =>
            {
                bool revoked = !response.IsTransportFailure &&
                    (response.StatusCode == 204 || response.StatusCode == 401);
                if (!revoked) _log.Warning("Hullbreach platform: could not revoke the signed-out session.");
                onComplete(revoked);
            });
        }

        // Reports a finished match. Safe to resend unchanged after a transient failure: the
        // platform keys on match_id, so a replay answers 200 and rates nobody twice.
        // frob:doc docs/platform-link.md#reporting-a-finished-match
        public void PostMatchResult(MatchResultReport report, Action<MatchReportOutcome> onComplete)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            if (onComplete == null) throw new ArgumentNullException(nameof(onComplete));
            string invalid = report.Validate();
            if (invalid != null)
            {
                _log.Warning($"Hullbreach platform: match report not sent: {invalid}.");
                onComplete(new MatchReportOutcome(MatchReportStatus.InvalidReport, report.MatchId, invalid));
                return;
            }
            if (!_config.HasServerKey)
            {
                const string noKey = "no game server key configured (" + PlatformConfig.ServerKeyEnv + ")";
                _log.Warning($"Hullbreach platform: match report not sent: {noKey}.");
                onComplete(new MatchReportOutcome(MatchReportStatus.Rejected, report.MatchId, noKey));
                return;
            }

            var headers = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>(ServerKeyHeader, _config.ServerKey),
            };
            var request = new PlatformHttpRequest(PlatformHttpMethod.Post, _config.BaseUrl + MatchesPath,
                report.ToJson(), headers, _config.TimeoutSeconds);
            _http.Send(request, response =>
            {
                MatchReportOutcome outcome = MapMatchReport(report.MatchId, response);
                if (outcome.IsStored)
                    _log.Info($"Hullbreach platform: match report {outcome}.");
                else
                    _log.Warning($"Hullbreach platform: match report failed: {outcome}.");
                onComplete(outcome);
            });
        }

        // frob:doc docs/platform-link.md#failure-modes
        public static MatchReportOutcome MapMatchReport(string matchId, PlatformHttpResponse response)
        {
            if (response.IsTransportFailure)
                return new MatchReportOutcome(MatchReportStatus.Unreachable, matchId,
                    "platform unreachable (" + response.TransportError + ")");

            string detail = Detail(response.Body);
            string suffix = detail.Length > 0 ? ": " + detail : string.Empty;
            switch (response.StatusCode)
            {
                case 201:
                    return new MatchReportOutcome(MatchReportStatus.Recorded, matchId, "match recorded");
                case 200:
                    return new MatchReportOutcome(MatchReportStatus.AlreadyRecorded, matchId,
                        "match was already recorded");
                case 404:
                    return new MatchReportOutcome(MatchReportStatus.UnknownPlayer, matchId,
                        "a reported player has no platform account" + suffix);
                case 409:
                    return new MatchReportOutcome(MatchReportStatus.Conflict, matchId,
                        "match_id was already used with a different report" + suffix);
                case 401:
                    return new MatchReportOutcome(MatchReportStatus.Rejected, matchId,
                        "platform refused this game server's key" + suffix + "; set " + PlatformConfig.ServerKeyEnv +
                        " to one of the platform's game server keys");
                case 422:
                    return new MatchReportOutcome(MatchReportStatus.Rejected, matchId,
                        "platform rejected the report body (HTTP 422)" + suffix);
                case 503:
                    return new MatchReportOutcome(MatchReportStatus.ServerError, matchId,
                        "platform cannot take match reports right now (HTTP 503)" + suffix);
                default:
                    return new MatchReportOutcome(
                        response.StatusCode >= 500 ? MatchReportStatus.ServerError : MatchReportStatus.Rejected,
                        matchId, "match report failed (HTTP " + response.StatusCode + ")");
            }
        }

        // frob:doc docs/platform-link.md#failure-modes
        public static LoginOutcome MapLogin(string typedUsername, PlatformHttpResponse response)
        {
            if (response.IsTransportFailure)
                return new LoginOutcome(LoginStatus.Unreachable, null, typedUsername,
                    "Cannot reach the Hullbreach servers (" + response.TransportError + ").");

            switch (response.StatusCode)
            {
                case 200:
                    if (PlatformJson.TryParseObject(response.Body, out var root))
                    {
                        string token = PlatformJson.GetString(root, "token");
                        string name = PlatformJson.GetString(PlatformJson.GetObject(root, "user"), "username");
                        if (!string.IsNullOrEmpty(token))
                            return new LoginOutcome(LoginStatus.Success, token,
                                string.IsNullOrEmpty(name) ? typedUsername : name, "Signed in.");
                    }
                    return new LoginOutcome(LoginStatus.Failed, null, typedUsername,
                        "The server sent an unexpected sign-in response.");
                case 401:
                    return new LoginOutcome(LoginStatus.InvalidCredentials, null, typedUsername,
                        "Wrong username or password.");
                case 429:
                    return new LoginOutcome(LoginStatus.RateLimited, null, typedUsername,
                        "Too many sign-in attempts. Wait a minute and try again.");
                case 422:
                    return new LoginOutcome(LoginStatus.Failed, null, typedUsername,
                        "The server rejected the sign-in request (HTTP 422).");
                default:
                    return new LoginOutcome(LoginStatus.Failed, null, typedUsername,
                        "Sign-in failed (HTTP " + response.StatusCode + ").");
            }
        }

        // frob:doc docs/platform-link.md#failure-modes
        public static SessionOutcome MapSession(PlatformHttpResponse response)
        {
            if (response.IsTransportFailure)
                return new SessionOutcome(SessionStatus.Unreachable, null, null,
                    "platform unreachable (" + response.TransportError + ")");

            switch (response.StatusCode)
            {
                case 200:
                    if (PlatformJson.TryParseObject(response.Body, out var root))
                    {
                        string userId = PlatformJson.GetString(root, "user_id");
                        if (!string.IsNullOrEmpty(userId))
                            return new SessionOutcome(SessionStatus.Valid, userId,
                                PlatformJson.GetString(root, "role"), "session valid");
                    }
                    return new SessionOutcome(SessionStatus.Failed, null, null, "unexpected session response");
                case 401:
                    // The platform checks X-Server-Key before the token and says which one
                    // failed in "detail"; a key problem is the operator's, not the player's.
                    string detail = Detail(response.Body);
                    if (detail.IndexOf("server key", StringComparison.OrdinalIgnoreCase) >= 0)
                        return new SessionOutcome(SessionStatus.Failed, null, null,
                            "platform refused this game server's key (" + detail + "); set " +
                            PlatformConfig.ServerKeyEnv + " to one of the platform's game server keys");
                    return new SessionOutcome(SessionStatus.Invalid, null, null, "platform session invalid or expired");
                case 503:
                    string unavailable = Detail(response.Body);
                    return new SessionOutcome(SessionStatus.Failed, null, null,
                        unavailable.IndexOf("server key", StringComparison.OrdinalIgnoreCase) >= 0
                            ? "platform has no game server keys configured (" + unavailable + ")"
                            : "session check failed (HTTP 503)");
                default:
                    return new SessionOutcome(SessionStatus.Failed, null, null,
                        "session check failed (HTTP " + response.StatusCode + ")");
            }
        }

        // The platform's error body is {"detail": str}; empty when absent or not JSON.
        static string Detail(string body) =>
            PlatformJson.TryParseObject(body, out var root) ? PlatformJson.GetString(root, "detail") ?? string.Empty : string.Empty;
    }
}
