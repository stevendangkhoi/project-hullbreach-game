using Hullbreach.Platform;
using NUnit.Framework;

namespace Hullbreach.Platform.Tests
{
    // Request shape, every status mapping, and that secrets never reach the log.
    public sealed class PlatformClientTests
    {
        const string Token = "tok-SECRET-123";
        const string Password = "pw-SECRET-456";
        const string Key = "key-SECRET-789";

        static PlatformClient Client(FakePlatformHttp http, RecordingPlatformLog log, string serverKey = null) =>
            new PlatformClient(new PlatformConfig("http://platform.test:8002/", 3, false, serverKey), http, log);

        static LoginOutcome LoginWith(PlatformHttpResponse response, out FakePlatformHttp http, out RecordingPlatformLog log)
        {
            http = new FakePlatformHttp();
            log = new RecordingPlatformLog();
            http.Enqueue(response);
            LoginOutcome result = default;
            Client(http, log).Login(" pilot ", Password, outcome => result = outcome);
            return result;
        }

        [Test]
        public void Login_PostsJsonCredentialsToLoginPath()
        {
            LoginWith(PlatformHttpResponse.Http(401, "{}"), out var http, out _);
            Assert.AreEqual(1, http.Requests.Count);
            var request = http.Requests[0];
            Assert.AreEqual(PlatformHttpMethod.Post, request.Method);
            Assert.AreEqual("http://platform.test:8002/api/v1/auth/login", request.Url);
            Assert.AreEqual("{\"username\":\"pilot\",\"password\":\"" + Password + "\"}", request.Body);
            Assert.AreEqual(3.0, request.TimeoutSeconds);
            Assert.IsNull(request.Header("Authorization"));
        }

        [Test]
        public void Login_Success_ReturnsTokenAndCanonicalUsername()
        {
            var outcome = LoginWith(PlatformHttpResponse.Http(200,
                "{\"token\":\"" + Token + "\",\"user\":{\"id\":\"u1\",\"username\":\"Pilot\"}}"), out _, out var log);
            Assert.AreEqual(LoginStatus.Success, outcome.Status);
            Assert.IsTrue(outcome.IsSuccess);
            Assert.AreEqual(Token, outcome.Token);
            Assert.AreEqual("Pilot", outcome.Username);
            StringAssert.DoesNotContain("SECRET", outcome.ToString());
            StringAssert.DoesNotContain("SECRET", log.All);
            StringAssert.Contains("login succeeded for 'Pilot'", log.All);
        }

        [TestCase(401, LoginStatus.InvalidCredentials, "Wrong username or password.")]
        [TestCase(429, LoginStatus.RateLimited, "Too many sign-in attempts. Wait a minute and try again.")]
        [TestCase(422, LoginStatus.Failed, "The server rejected the sign-in request (HTTP 422).")]
        [TestCase(503, LoginStatus.Failed, "Sign-in failed (HTTP 503).")]
        public void Login_MapsErrorStatuses(int code, LoginStatus expected, string message)
        {
            var outcome = LoginWith(PlatformHttpResponse.Http(code, "{\"detail\":\"x\"}"), out _, out var log);
            Assert.AreEqual(expected, outcome.Status);
            Assert.AreEqual(message, outcome.Message);
            Assert.AreEqual("pilot", outcome.Username);
            Assert.AreEqual(string.Empty, outcome.Token);
            StringAssert.DoesNotContain("SECRET", log.All);
        }

        [Test]
        public void Login_TransportFailure_IsUnreachable()
        {
            var outcome = LoginWith(PlatformHttpResponse.Transport("Cannot connect to destination host"), out _, out var log);
            Assert.AreEqual(LoginStatus.Unreachable, outcome.Status);
            StringAssert.Contains("Cannot reach the Hullbreach servers", outcome.Message);
            StringAssert.DoesNotContain("SECRET", log.All);
        }

        [TestCase("not json")]
        [TestCase("{\"user\":{\"username\":\"pilot\"}}")]
        [TestCase("{\"token\":\"\"}")]
        public void Login_UnexpectedSuccessBody_IsFailed(string body)
        {
            var outcome = LoginWith(PlatformHttpResponse.Http(200, body), out _, out _);
            Assert.AreEqual(LoginStatus.Failed, outcome.Status);
            Assert.AreEqual(string.Empty, outcome.Token);
        }

        [Test]
        public void Login_SuccessWithoutUsername_FallsBackToTyped()
        {
            var outcome = LoginWith(PlatformHttpResponse.Http(200, "{\"token\":\"" + Token + "\"}"), out _, out _);
            Assert.IsTrue(outcome.IsSuccess);
            Assert.AreEqual("pilot", outcome.Username);
        }

        [TestCase("", "pw")]
        [TestCase("   ", "pw")]
        [TestCase("pilot", "")]
        [TestCase(null, null)]
        public void Login_EmptyFields_ShortCircuitWithoutHttp(string user, string pass)
        {
            var http = new FakePlatformHttp();
            LoginOutcome result = default;
            Client(http, new RecordingPlatformLog()).Login(user, pass, outcome => result = outcome);
            Assert.AreEqual(0, http.Requests.Count);
            Assert.AreEqual(LoginStatus.InvalidCredentials, result.Status);
            Assert.AreEqual("Enter a username and password.", result.Message);
        }

        [Test]
        public void ValidateSession_SendsBearerAndServerKey()
        {
            var http = new FakePlatformHttp();
            var log = new RecordingPlatformLog();
            http.Enqueue(PlatformHttpResponse.Http(200, "{\"user_id\":\"0f8e-uuid\",\"role\":\"player\"}"));
            SessionOutcome result = default;
            Client(http, log, Key).ValidateSession(Token, outcome => result = outcome);

            var request = http.Requests[0];
            Assert.AreEqual(PlatformHttpMethod.Get, request.Method);
            Assert.AreEqual("http://platform.test:8002/api/v1/auth/session", request.Url);
            Assert.IsNull(request.Body);
            Assert.AreEqual("Bearer " + Token, request.Header("authorization"));
            Assert.AreEqual(Key, request.Header(PlatformClient.ServerKeyHeader));
            Assert.AreEqual(SessionStatus.Valid, result.Status);
            Assert.AreEqual("0f8e-uuid", result.UserId);
            Assert.AreEqual("player", result.Role);
            StringAssert.DoesNotContain("SECRET", log.All);
        }

        [Test]
        public void ValidateSession_WithoutServerKey_OmitsHeader()
        {
            var http = new FakePlatformHttp();
            http.Enqueue(PlatformHttpResponse.Http(401, "{\"detail\":\"not authenticated\"}"));
            SessionOutcome result = default;
            Client(http, new RecordingPlatformLog()).ValidateSession(Token, outcome => result = outcome);
            Assert.IsNull(http.Requests[0].Header(PlatformClient.ServerKeyHeader));
            Assert.AreEqual(SessionStatus.Invalid, result.Status);
        }

        [Test]
        public void ValidateSession_EmptyToken_IsInvalidWithoutHttp()
        {
            var http = new FakePlatformHttp();
            SessionOutcome result = default;
            Client(http, new RecordingPlatformLog()).ValidateSession("", outcome => result = outcome);
            Assert.AreEqual(0, http.Requests.Count);
            Assert.AreEqual(SessionStatus.Invalid, result.Status);
        }

        [Test]
        public void RestoreSession_UsesOwnProfileWithoutServerKey()
        {
            // frob:tests Assets/Scripts/Hullbreach.Platform/PlatformClient.cs::PlatformClient.RestoreSession kind="unit"
            var http = new FakePlatformHttp();
            var log = new RecordingPlatformLog();
            http.Enqueue(PlatformHttpResponse.Http(200, "{\"id\":\"u-1\",\"username\":\"renamed-pilot\",\"role\":\"player\"}"));
            LoginOutcome result = default;
            Client(http, log, Key).RestoreSession(Token, outcome => result = outcome);

            Assert.AreEqual("http://platform.test:8002/api/v1/me", http.Requests[0].Url);
            Assert.AreEqual(PlatformHttpMethod.Get, http.Requests[0].Method);
            Assert.AreEqual("Bearer " + Token, http.Requests[0].Header("Authorization"));
            Assert.IsNull(http.Requests[0].Header(PlatformClient.ServerKeyHeader));
            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual("renamed-pilot", result.Username);
            Assert.AreEqual(Token, result.Token);
            StringAssert.DoesNotContain("SECRET", log.All);
        }

        [TestCase(401, "{}", LoginStatus.InvalidCredentials)]
        [TestCase(500, "{}", LoginStatus.Failed)]
        [TestCase(200, "not json", LoginStatus.Failed)]
        [TestCase(200, "{\"id\":\"u-1\"}", LoginStatus.Failed)]
        [TestCase(200, "{\"username\":\"pilot\"}", LoginStatus.Failed)]
        public void RestoreSession_RejectsExpiredOrMalformedProfile(int status, string body, LoginStatus expected)
        {
            var http = new FakePlatformHttp();
            http.Enqueue(PlatformHttpResponse.Http(status, body));
            LoginOutcome result = default;
            Client(http, new RecordingPlatformLog()).RestoreSession(Token, outcome => result = outcome);
            Assert.AreEqual(expected, result.Status);
            Assert.IsEmpty(result.Token);
        }

        [Test]
        public void RestoreSession_EmptyTokenSendsNothing()
        {
            var http = new FakePlatformHttp();
            LoginOutcome result = default;
            Client(http, new RecordingPlatformLog()).RestoreSession("", outcome => result = outcome);
            Assert.AreEqual(LoginStatus.InvalidCredentials, result.Status);
            Assert.IsEmpty(http.Requests);
        }

        [TestCase(204, true)]
        [TestCase(401, true)]
        [TestCase(500, false)]
        public void Logout_RevokesOnlyThePresentedSession(int status, bool expected)
        {
            // frob:tests Assets/Scripts/Hullbreach.Platform/PlatformClient.cs::PlatformClient.Logout kind="unit"
            var http = new FakePlatformHttp();
            var log = new RecordingPlatformLog();
            http.Enqueue(PlatformHttpResponse.Http(status, ""));
            bool revoked = !expected;
            Client(http, log, Key).Logout(Token, result => revoked = result);

            Assert.AreEqual(expected, revoked);
            Assert.AreEqual(PlatformHttpMethod.Post, http.Requests[0].Method);
            Assert.AreEqual("http://platform.test:8002/api/v1/auth/logout", http.Requests[0].Url);
            Assert.AreEqual("Bearer " + Token, http.Requests[0].Header("Authorization"));
            Assert.IsNull(http.Requests[0].Header(PlatformClient.ServerKeyHeader));
            StringAssert.DoesNotContain("SECRET", log.All);
        }

        [Test]
        public void Logout_ReportsTransportFailureAndSkipsEmptyTokens()
        {
            var http = new FakePlatformHttp();
            http.Enqueue(PlatformHttpResponse.Transport("offline"));
            var client = Client(http, new RecordingPlatformLog());
            bool revoked = true;
            client.Logout(Token, result => revoked = result);
            Assert.IsFalse(revoked);
            client.Logout("", result => revoked = result);
            Assert.IsTrue(revoked);
            Assert.AreEqual(1, http.Requests.Count);
        }

        [Test]
        public void MapSession_CoversEveryBranch()
        {
            Assert.AreEqual(SessionStatus.Unreachable, PlatformClient.MapSession(PlatformHttpResponse.Transport("timeout")).Status);
            Assert.AreEqual(SessionStatus.Invalid, PlatformClient.MapSession(PlatformHttpResponse.Http(401, "")).Status);
            Assert.AreEqual(SessionStatus.Failed, PlatformClient.MapSession(PlatformHttpResponse.Http(500, "")).Status);
            Assert.AreEqual(SessionStatus.Failed, PlatformClient.MapSession(PlatformHttpResponse.Http(200, "{\"role\":\"player\"}")).Status);
            Assert.AreEqual(SessionStatus.Failed, PlatformClient.MapSession(PlatformHttpResponse.Http(200, "<html>")).Status);
        }

        [TestCase("server key missing")]
        [TestCase("invalid server key")]
        public void MapSession_ServerKeyRejection_IsAnOperatorFailure_NotAnExpiredSession(string detail)
        {
            var outcome = PlatformClient.MapSession(PlatformHttpResponse.Http(401, "{\"detail\":\"" + detail + "\"}"));
            Assert.AreEqual(SessionStatus.Failed, outcome.Status);
            StringAssert.Contains(detail, outcome.Message);
            StringAssert.Contains(PlatformConfig.ServerKeyEnv, outcome.Message);
        }

        [Test]
        public void MapSession_BadTokenWithGoodKey_IsInvalid()
        {
            var outcome = PlatformClient.MapSession(PlatformHttpResponse.Http(401, "{\"detail\":\"not authenticated\"}"));
            Assert.AreEqual(SessionStatus.Invalid, outcome.Status);
        }

        [Test]
        public void MapSession_PlatformWithoutKeys_SaysSo()
        {
            var outcome = PlatformClient.MapSession(PlatformHttpResponse.Http(503, "{\"detail\":\"server key not configured\"}"));
            Assert.AreEqual(SessionStatus.Failed, outcome.Status);
            StringAssert.Contains("no game server keys", outcome.Message);
            Assert.AreEqual(SessionStatus.Failed, PlatformClient.MapSession(PlatformHttpResponse.Http(503, "")).Status);
        }
    }
}
