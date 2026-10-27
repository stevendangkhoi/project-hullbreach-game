using System.IO;
using Hullbreach.Platform;
using NUnit.Framework;

namespace Hullbreach.Platform.Tests
{
    // S07-3: the token survives a relaunch, is validated before reuse, and is presented at join.
    public sealed class SessionTokenStoreTests
    {
        const string Token = "tok-ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789abcd";

        string _dir;
        string _path;
        FakePlatformHttp _http;
        RecordingPlatformLog _log;

        [SetUp]
        public void SetUp()
        {
            PlatformSession.SignOut();
            _dir = Path.Combine(Path.GetTempPath(), "hullbreach-store-" + Path.GetRandomFileName());
            _path = Path.Combine(_dir, "nested", "platform-session.txt");
            _http = new FakePlatformHttp();
            _log = new RecordingPlatformLog();
        }

        [TearDown]
        public void TearDown()
        {
            PlatformSession.SignOut();
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        SessionTokenStore NewStore() => new SessionTokenStore(_path, _log);

        SignInModel NewModel() =>
            new SignInModel(new PlatformClient(new PlatformConfig("http://platform.test"), _http, _log), _log, NewStore());

        [Test]
        public void SaveThenLoad_RoundTripsAcrossInstances()
        {
            Assert.IsTrue(NewStore().Save("Pilot", Token));
            Assert.IsTrue(NewStore().TryLoad(out string user, out string token));
            Assert.AreEqual("Pilot", user);
            Assert.AreEqual(Token, token);
        }

        [Test]
        public void Load_WithNothingStored_ReturnsFalse()
        {
            Assert.IsFalse(NewStore().TryLoad(out string user, out string token));
            Assert.AreEqual(string.Empty, user);
            Assert.AreEqual(string.Empty, token);
        }

        [Test]
        public void Save_RefusesEmptyTokenAndMultilineUsername()
        {
            Assert.IsFalse(NewStore().Save("Pilot", ""));
            Assert.IsFalse(NewStore().Save("Pi\nlot", Token));
            Assert.IsFalse(File.Exists(_path));
        }

        [Test]
        public void Load_DiscardsMalformedFile()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            File.WriteAllText(_path, "only-one-line");
            Assert.IsFalse(NewStore().TryLoad(out _, out _));
            Assert.IsFalse(File.Exists(_path));
        }

        [Test]
        public void Clear_RemovesFileAndIsIdempotent()
        {
            NewStore().Save("Pilot", Token);
            NewStore().Clear();
            NewStore().Clear();
            Assert.IsFalse(File.Exists(_path));
        }

        [Test]
        public void StoreNeverLogsToken()
        {
            var store = NewStore();
            store.Save("Pilot", Token);
            store.TryLoad(out _, out _);
            store.Clear();
            StringAssert.DoesNotContain(Token, _log.All);
        }

        [Test]
        public void Save_UnwritableLocationFailsSoftly()
        {
            Directory.CreateDirectory(_dir);
            string blocker = Path.Combine(_dir, "file");
            File.WriteAllText(blocker, "x");
            var store = new SessionTokenStore(Path.Combine(blocker, "sub", "s.txt"), _log);
            Assert.IsFalse(store.Save("Pilot", Token));
            StringAssert.Contains("could not save", _log.All);
        }

        [Test]
        public void SuccessfulSignIn_PersistsSession()
        {
            _http.Enqueue(PlatformHttpResponse.Http(200,
                "{\"token\":\"" + Token + "\",\"user\":{\"id\":\"u-1\",\"username\":\"Pilot\"}}"));
            NewModel().Submit("pilot", "pw");
            Assert.IsTrue(NewStore().TryLoad(out string user, out string token));
            Assert.AreEqual("Pilot", user);
            Assert.AreEqual(Token, token);
        }

        [Test]
        public void FailedSignIn_StoresNothing()
        {
            _http.Enqueue(PlatformHttpResponse.Http(401, "{\"detail\":\"bad\"}"));
            NewModel().Submit("pilot", "pw");
            Assert.IsFalse(File.Exists(_path));
        }

        [Test]
        public void Restore_ValidTokenSignsInAndSendsBearer()
        {
            NewStore().Save("Pilot", Token);
            _http.Enqueue(PlatformHttpResponse.Http(200, "{\"id\":\"u-1\",\"username\":\"Pilot\",\"role\":\"player\"}"));
            var model = NewModel();
            model.TryRestore();

            Assert.AreEqual(SignInState.SignedIn, model.State);
            Assert.AreEqual("Pilot", PlatformSession.Username);
            // The join handshake reads this token from PlatformSession.
            Assert.AreEqual(Token, PlatformSession.Token);
            Assert.AreEqual("Bearer " + Token, _http.Requests[0].Header("authorization"));
            Assert.AreEqual("http://platform.test/api/v1/me", _http.Requests[0].Url);
            Assert.IsNull(_http.Requests[0].Header(PlatformClient.ServerKeyHeader));
            StringAssert.DoesNotContain(Token, _log.All);
        }

        [Test]
        public void Restore_RejectedTokenIsForgottenAndFormShown()
        {
            NewStore().Save("Pilot", Token);
            _http.Enqueue(PlatformHttpResponse.Http(401, "{\"detail\":\"expired\"}"));
            var model = NewModel();
            model.TryRestore();

            Assert.AreEqual(SignInState.Editing, model.State);
            Assert.IsFalse(PlatformSession.IsSignedIn);
            Assert.IsFalse(File.Exists(_path));
            StringAssert.Contains("expired", model.Message);
        }

        [Test]
        public void Restore_UnreachablePlatformKeepsTokenForNextLaunch()
        {
            NewStore().Save("Pilot", Token);
            _http.Enqueue(PlatformHttpResponse.Transport("down"));
            var model = NewModel();
            model.TryRestore();

            Assert.AreEqual(SignInState.Editing, model.State);
            Assert.IsFalse(PlatformSession.IsSignedIn);
            Assert.IsTrue(File.Exists(_path));
        }

        [Test]
        public void Restore_WithNothingStoredStaysEditingWithoutRequest()
        {
            var model = NewModel();
            model.TryRestore();
            Assert.AreEqual(SignInState.Editing, model.State);
            Assert.AreEqual(0, _http.Requests.Count);
        }

        [Test]
        public void Restore_PlayOfflineWhileBusyDropsLateAnswer()
        {
            NewStore().Save("Pilot", Token);
            _http.Hold = true;
            var model = NewModel();
            model.TryRestore();
            Assert.AreEqual(SignInState.Busy, model.State);
            model.PlayOffline();
            _http.Complete(0, PlatformHttpResponse.Http(200, "{\"id\":\"u-1\",\"username\":\"Pilot\",\"role\":\"player\"}"));

            Assert.AreEqual(SignInState.Offline, model.State);
            Assert.IsFalse(PlatformSession.IsSignedIn);
        }

        [Test]
        public void SignOut_ForgetsRememberedSession()
        {
            NewStore().Save("Pilot", Token);
            _http.Enqueue(PlatformHttpResponse.Http(200, "{\"id\":\"u-1\",\"username\":\"Pilot\",\"role\":\"player\"}"));
            var model = NewModel();
            model.TryRestore();
            model.SignOut();
            Assert.IsFalse(File.Exists(_path));
        }

        [Test]
        public void Restore_UsesCurrentProfileNameAndUpdatesStoredName()
        {
            NewStore().Save("old-name", Token);
            _http.Enqueue(PlatformHttpResponse.Http(200, "{\"id\":\"u-1\",\"username\":\"new-name\"}"));
            NewModel().TryRestore();

            Assert.AreEqual("new-name", PlatformSession.Username);
            Assert.IsTrue(NewStore().TryLoad(out string username, out _));
            Assert.AreEqual("new-name", username);
        }

        [Test]
        public void Restore_ClosedScreenCannotRestoreTokenLater()
        {
            NewStore().Save("Pilot", Token);
            _http.Hold = true;
            var model = NewModel();
            model.TryRestore();
            model.CancelPending();
            _http.Complete(0, PlatformHttpResponse.Http(200, "{\"id\":\"u-1\",\"username\":\"Pilot\"}"));

            Assert.IsFalse(PlatformSession.IsSignedIn);
            Assert.IsTrue(File.Exists(_path));
        }

        [Test]
        public void PlayOffline_KeepsRememberedSession()
        {
            NewStore().Save("Pilot", Token);
            NewModel().PlayOffline();
            Assert.IsTrue(File.Exists(_path));
        }
    }
}
