using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using Hullbreach.Platform;
using NUnit.Framework;

namespace Hullbreach.Platform.Tests
{
    // End-to-end proof against a real platform API; skipped unless HULLBREACH_PLATFORM_IT_URL
    // is set. Needs HULLBREACH_PLATFORM_IT_USER/_PASSWORD for an existing account, and
    // HULLBREACH_PLATFORM_SERVER_KEY for the server-side check. Never prints secrets.
    [Category("Integration")]
    public sealed class PlatformLiveIntegrationTests
    {
        const string UrlEnv = "HULLBREACH_PLATFORM_IT_URL";
        const string UserEnv = "HULLBREACH_PLATFORM_IT_USER";
        const string PasswordEnv = "HULLBREACH_PLATFORM_IT_PASSWORD";

        // Blocking HttpClient transport: fine for a test, which has no frame loop to stall.
        sealed class BlockingHttp : IPlatformHttp
        {
            static readonly HttpClient Http = new HttpClient();

            public void Send(PlatformHttpRequest request, Action<PlatformHttpResponse> onComplete)
            {
                var message = new HttpRequestMessage(
                    request.Method == PlatformHttpMethod.Post ? HttpMethod.Post
                    : request.Method == PlatformHttpMethod.Delete ? HttpMethod.Delete : HttpMethod.Get, request.Url);
                if (request.Body != null)
                    message.Content = new StringContent(request.Body, Encoding.UTF8, "application/json");
                foreach (var header in request.Headers)
                    message.Headers.TryAddWithoutValidation(header.Key, header.Value);
                PlatformHttpResponse response;
                try
                {
                    using (var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(request.TimeoutSeconds)))
                    {
                        HttpResponseMessage answer = Http.SendAsync(message, cts.Token).GetAwaiter().GetResult();
                        response = PlatformHttpResponse.Http((int)answer.StatusCode,
                            answer.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                    }
                }
                catch (Exception e) when (e is HttpRequestException || e is OperationCanceledException)
                {
                    response = PlatformHttpResponse.Transport(e.GetType().Name + ": " + e.Message);
                }
                onComplete(response);
            }
        }

        sealed class ConsoleLog : IPlatformLog
        {
            public readonly List<string> Lines = new List<string>();

            public void Info(string message) => Write("INFO " + message);

            public void Warning(string message) => Write("WARN " + message);

            void Write(string line)
            {
                Lines.Add(line);
                TestContext.Progress.WriteLine("[platform-it] " + line);
            }
        }

        static string Env(string name) => Environment.GetEnvironmentVariable(name);

        // Reads the caller's rating from GET /api/v1/me.
        static int RatingOf(string url, string token)
        {
            using (var http = new HttpClient())
            {
                var message = new HttpRequestMessage(HttpMethod.Get, url + "/api/v1/me");
                message.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
                string body = http.SendAsync(message).GetAwaiter().GetResult().Content.ReadAsStringAsync().GetAwaiter().GetResult();
                var match = System.Text.RegularExpressions.Regex.Match(body, "\"rating\"\\s*:\\s*(\\d+)");
                Assert.IsTrue(match.Success, "no rating in /me answer");
                return int.Parse(match.Groups[1].Value);
            }
        }

        // Second account for the report test; the first comes from the _USER/_PASSWORD pair.
        const string User2Env = "HULLBREACH_PLATFORM_IT_USER2";
        const string Password2Env = "HULLBREACH_PLATFORM_IT_PASSWORD2";

        // Reports one match from two real sessions and checks the platform answers 201 and moves both ratings.
        // frob:doc docs/platform-link.md#reporting-a-finished-match
        [Test]
        public void ReporterPostsAMatch_AgainstRealPlatform_Is201AndMovesRatings()
        {
            string url = Env(UrlEnv);
            if (string.IsNullOrEmpty(url))
                Assert.Ignore(UrlEnv + " not set; live platform test skipped.");
            if (string.IsNullOrEmpty(Env(User2Env)))
                Assert.Ignore(User2Env + " not set; live report test skipped.");

            var log = new ConsoleLog();
            var http = new BlockingHttp();
            var clientConfig = PlatformConfig.FromEnvironment(new[] { PlatformConfig.UrlArg, url }, _ => null, log);
            var serverConfig = PlatformConfig.FromEnvironment(new[] { PlatformConfig.UrlArg, url, PlatformConfig.RequireAuthArg },
                Env, log);
            Assert.IsTrue(serverConfig.HasServerKey, "HULLBREACH_PLATFORM_SERVER_KEY must be set.");

            LoginOutcome a = default, b = default;
            new PlatformClient(clientConfig, http, log).Login(Env(UserEnv), Env(PasswordEnv), o => a = o);
            new PlatformClient(clientConfig, http, log).Login(Env(User2Env), Env(Password2Env), o => b = o);
            Assert.AreEqual(LoginStatus.Success, a.Status, a.Message);
            Assert.AreEqual(LoginStatus.Success, b.Status, b.Message);
            int beforeA = RatingOf(url, a.Token), beforeB = RatingOf(url, b.Token);

            var serverClient = new PlatformClient(serverConfig, http, log);
            var gate = new PlatformAdmissionGate(serverClient, true, serverConfig.HelloTimeoutSeconds, log);
            gate.OnConnected(1, 0);
            gate.OnHello(1, a.Token, a.Username, 0.1);
            gate.OnConnected(2, 0);
            gate.OnHello(2, b.Token, b.Username, 0.1);

            var reporter = new MatchResultReporter(serverClient, gate, log);
            string matchId = Guid.NewGuid().ToString();
            Assert.IsTrue(reporter.ReportFinished(matchId, 1, 1, new PlayerMatchStats(null, 900, 14, 30, 240), 2,
                new PlayerMatchStats(null, 400, 12, 28, 200), 240, 0));
            Assert.AreEqual(0, reporter.PendingCount);
            Assert.IsTrue(reporter.TryTakeFinished(out var outcome));
            Say("report outcome: " + outcome);
            Assert.AreEqual(MatchReportStatus.Recorded, outcome.Status);

            int afterA = RatingOf(url, a.Token), afterB = RatingOf(url, b.Token);
            Say($"ratings: {a.Username} {beforeA}->{afterA}, {b.Username} {beforeB}->{afterB}");
            Assert.Greater(afterA, beforeA);
            Assert.Less(afterB, beforeB);

            foreach (string line in log.Lines)
            {
                StringAssert.DoesNotContain(a.Token, line);
                StringAssert.DoesNotContain(serverConfig.ServerKey, line);
            }
        }

        static void Say(string line) => TestContext.Progress.WriteLine("[platform-it] " + line);

        [Test]
        public void LoginThenServerValidatesToken_AgainstRealPlatform()
        {
            string url = Env(UrlEnv);
            if (string.IsNullOrEmpty(url))
                Assert.Ignore(UrlEnv + " not set; live platform test skipped.");
            string user = Env(UserEnv);
            string password = Env(PasswordEnv);
            Assert.IsFalse(string.IsNullOrEmpty(user) || string.IsNullOrEmpty(password),
                UserEnv + " and " + PasswordEnv + " must be set for the live test.");

            var log = new ConsoleLog();
            var http = new BlockingHttp();
            var clientConfig = PlatformConfig.FromEnvironment(new[] { PlatformConfig.UrlArg, url }, _ => null, log);
            var serverConfig = PlatformConfig.FromEnvironment(new[] { PlatformConfig.UrlArg, url, PlatformConfig.RequireAuthArg },
                Env, log);
            Say("client config: " + clientConfig.Describe());
            Say("server config: " + serverConfig.Describe());

            // Wrong password first: proves the 401 mapping against the real API.
            LoginOutcome wrong = default;
            new PlatformClient(clientConfig, http, log).Login(user, password + "-wrong", o => wrong = o);
            Say("wrong-password login: " + wrong);
            Assert.AreEqual(LoginStatus.InvalidCredentials, wrong.Status);

            LoginOutcome login = default;
            new PlatformClient(clientConfig, http, log).Login(user, password, o => login = o);
            Say("login: " + login + " token=" + (login.Token.Length > 0 ? "<redacted, " + login.Token.Length + " chars>" : "<none>"));
            Assert.AreEqual(LoginStatus.Success, login.Status, login.Message);

            // a normal client restores its session without the game server's key.
            var playerClient = new PlatformClient(clientConfig, http, log);
            LoginOutcome restored = default;
            playerClient.RestoreSession(login.Token, o => restored = o);
            Assert.AreEqual(LoginStatus.Success, restored.Status, restored.Message);
            Assert.AreEqual(login.Username, restored.Username);
            Assert.AreEqual(login.Token, restored.Token);
            Say("saved session restored through /me without a server key.");

            // Server side: the gate in required mode, exactly as the dedicated server runs it.
            var serverClient = new PlatformClient(serverConfig, http, log);
            var gate = new PlatformAdmissionGate(serverClient, true, serverConfig.HelloTimeoutSeconds, log);
            gate.OnConnected(1, 0);
            gate.OnHello(1, login.Token, login.Username, 0.1);
            Assert.IsTrue(gate.TryTakeDecision(out var admitted));
            Say("server decision (valid token): " + admitted);
            Assert.IsTrue(admitted.Admitted && admitted.Verified);
            Assert.AreEqual(login.Username, admitted.DisplayName);

            gate.OnConnected(2, 0);
            gate.OnHello(2, "not-a-real-token", "intruder", 0.1);
            Assert.IsTrue(gate.TryTakeDecision(out var rejected));
            Say("server decision (forged token): " + rejected);
            Assert.IsFalse(rejected.Admitted);

            bool loggedOut = false;
            playerClient.Logout(login.Token, ok => loggedOut = ok);
            Assert.IsTrue(loggedOut, "the platform did not revoke the session");
            playerClient.RestoreSession(login.Token, o => restored = o);
            Assert.AreEqual(LoginStatus.InvalidCredentials, restored.Status);
            gate.OnConnected(3, 0);
            gate.OnHello(3, login.Token, login.Username, 0.1);
            Assert.IsTrue(gate.TryTakeDecision(out var signedOut));
            Assert.IsFalse(signedOut.Admitted);
            Say("sign-out revoked the token for both restore and server admission.");

            foreach (string line in log.Lines)
            {
                StringAssert.DoesNotContain(login.Token, line);
                StringAssert.DoesNotContain(password, line);
                if (serverConfig.HasServerKey) StringAssert.DoesNotContain(serverConfig.ServerKey, line);
            }
            Say("no token, password or server key appeared in any log line.");
        }
    }
}
