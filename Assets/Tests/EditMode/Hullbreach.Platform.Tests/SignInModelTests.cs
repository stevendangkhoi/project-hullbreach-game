using System.Collections.Generic;
using Hullbreach.Platform;
using NUnit.Framework;

namespace Hullbreach.Platform.Tests
{
    // The sign-in screen's behaviour, driven through a fake platform.
    public sealed class SignInModelTests
    {
        const string Password = "hunter2-secret";
        const string Token = "tok-ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789abcd";

        FakePlatformHttp _http;
        RecordingPlatformLog _log;
        SignInModel _model;
        List<SignInState> _seen;

        [SetUp]
        public void SetUp()
        {
            PlatformSession.SignOut();
            _http = new FakePlatformHttp();
            _log = new RecordingPlatformLog();
            _model = NewModel();
            _seen = new List<SignInState>();
            _model.Changed += () => _seen.Add(_model.State);
        }

        [TearDown]
        public void TearDown() => PlatformSession.SignOut();

        SignInModel NewModel() =>
            new SignInModel(new PlatformClient(new PlatformConfig("http://platform.test"), _http, _log), _log);

        static PlatformHttpResponse LoginOk(string username) =>
            PlatformHttpResponse.Http(200,
                "{\"token\":\"" + Token + "\",\"user\":{\"id\":\"u-1\",\"username\":\"" + username + "\"}}");

        [Test]
        public void StartsEditingWithNoMessage()
        {
            Assert.AreEqual(SignInState.Editing, _model.State);
            Assert.IsTrue(_model.CanSubmit);
            Assert.IsFalse(_model.IsDone);
            Assert.AreEqual(string.Empty, _model.Message);
            Assert.AreEqual(string.Empty, _model.AccountLine);
            Assert.IsNull(_model.LastStatus);
        }

        // frob:ticket 01M3DG5Y1903PCF86DJBDSPXJD
        [Test]
        public void SubmitClearingBlanksPasswordFieldAfterEverySubmit()
        {
            string field = Password;
            _http.Enqueue(PlatformHttpResponse.Http(401, "{}"));
            _model.SubmitClearing("pilot", () => field, v => field = v);
            Assert.AreEqual(string.Empty, field);
            Assert.AreEqual(SignInState.Editing, _model.State);

            field = Password;
            _http.Enqueue(LoginOk("Pilot"));
            _model.SubmitClearing("pilot", () => field, v => field = v);
            Assert.AreEqual(string.Empty, field);
            Assert.AreEqual(SignInState.SignedIn, _model.State);
        }

        [Test]
        public void SuccessStoresTokenInMemoryAndShowsCanonicalName()
        {
            _http.Enqueue(LoginOk("Pilot"));
            _model.Submit(" pilot ", Password);

            Assert.AreEqual(SignInState.SignedIn, _model.State);
            Assert.IsTrue(_model.IsDone);
            Assert.IsFalse(_model.IsError);
            Assert.AreEqual(LoginStatus.Success, _model.LastStatus);
            Assert.AreEqual("Signed in as Pilot.", _model.Message);
            Assert.AreEqual("Signed in as Pilot", _model.AccountLine);
            Assert.IsTrue(PlatformSession.IsSignedIn);
            Assert.AreEqual(Token, PlatformSession.Token);
            Assert.AreEqual("Pilot", PlatformSession.Username);
            CollectionAssert.AreEqual(new[] { SignInState.Busy, SignInState.SignedIn }, _seen);
        }

        [TestCase(401, LoginStatus.InvalidCredentials, "Wrong username or password.")]
        [TestCase(429, LoginStatus.RateLimited, "Too many sign-in attempts. Wait a minute and try again.")]
        [TestCase(500, LoginStatus.Failed, "Sign-in failed (HTTP 500).")]
        public void HttpFailureReturnsToFormWithItsMessage(int code, LoginStatus status, string message)
        {
            _http.Enqueue(PlatformHttpResponse.Http(code, "{\"detail\":\"x\"}"));
            _model.Submit("pilot", Password);

            Assert.AreEqual(SignInState.Editing, _model.State);
            Assert.IsTrue(_model.CanSubmit);
            Assert.IsTrue(_model.IsError);
            Assert.AreEqual(status, _model.LastStatus);
            Assert.AreEqual(message, _model.Message);
            Assert.IsFalse(PlatformSession.IsSignedIn);
        }

        [Test]
        public void UnreachableSuggestsPlayingOffline()
        {
            _http.Enqueue(PlatformHttpResponse.Transport("Cannot connect to destination host"));
            _model.Submit("pilot", Password);

            Assert.AreEqual(LoginStatus.Unreachable, _model.LastStatus);
            StringAssert.StartsWith("Cannot reach the Hullbreach servers", _model.Message);
            StringAssert.EndsWith(SignInModel.UnreachableHint, _model.Message);
            Assert.IsTrue(_model.IsError);
        }

        [Test]
        public void EmptyFieldsAreRejectedWithoutARequest()
        {
            _model.Submit("  ", string.Empty);

            Assert.AreEqual(0, _http.Requests.Count);
            Assert.AreEqual(SignInState.Editing, _model.State);
            Assert.AreEqual("Enter a username and password.", _model.Message);
            Assert.IsTrue(_model.IsError);
        }

        [Test]
        public void SubmitWhileBusyIsIgnored()
        {
            _http.Hold = true;
            _model.Submit("pilot", Password);
            _model.Submit("pilot", Password);

            Assert.AreEqual(1, _http.Requests.Count);
            Assert.AreEqual(SignInState.Busy, _model.State);
            Assert.IsFalse(_model.CanSubmit);
            Assert.AreEqual("Signing in...", _model.Message);
        }

        [Test]
        public void PlayOfflineWhileBusyWinsOverALateSuccess()
        {
            _http.Hold = true;
            _model.Submit("pilot", Password);
            _model.PlayOffline();
            _http.Complete(0, LoginOk("pilot"));

            Assert.AreEqual(SignInState.Offline, _model.State);
            Assert.IsFalse(PlatformSession.IsSignedIn);
            Assert.IsTrue(PlatformSession.IsOffline);
            StringAssert.Contains("dropping a late Success answer", _log.All);
        }

        [Test]
        public void PlayOfflineMarksTheSessionAndShowsInAccountBar()
        {
            _model.PlayOffline();

            Assert.AreEqual(SignInState.Offline, _model.State);
            Assert.IsTrue(_model.IsDone);
            Assert.AreEqual("Playing offline", _model.AccountLine);
            Assert.IsTrue(PlatformSession.HasChosen);
            Assert.AreEqual(string.Empty, PlatformSession.Token);
            _model.PlayOffline();
            Assert.AreEqual(1, _seen.Count);
        }

        [Test]
        public void SignOutForgetsTokenAndReturnsToForm()
        {
            _http.Enqueue(LoginOk("pilot"));
            _model.Submit("pilot", Password);
            _http.Enqueue(PlatformHttpResponse.Http(204, ""));
            _model.SignOut();

            Assert.AreEqual(SignInState.Editing, _model.State);
            Assert.AreEqual("Signed out.", _model.Message);
            Assert.IsFalse(PlatformSession.HasChosen);
            Assert.AreEqual(string.Empty, PlatformSession.Token);
            Assert.IsNull(_model.LastStatus);
        }

        [Test]
        public void FailedLogoutStillClearsLocalSessionAndExplainsTheFailure()
        {
            _http.Enqueue(LoginOk("pilot"));
            _model.Submit("pilot", Password);
            _http.Enqueue(PlatformHttpResponse.Transport("offline"));
            _model.SignOut();

            Assert.IsFalse(PlatformSession.HasChosen);
            Assert.AreEqual(SignInState.Editing, _model.State);
            Assert.IsTrue(_model.IsError);
            StringAssert.Contains("could not revoke", _model.Message);
        }

        [Test]
        public void LateLogoutFailureCannotOverwriteAnotherLogin()
        {
            _http.Enqueue(LoginOk("pilot"));
            _model.Submit("pilot", Password);
            _http.Hold = true;
            _model.SignOut();
            _model.Submit("second-pilot", Password);
            _http.Complete(0, PlatformHttpResponse.Transport("offline"));

            Assert.AreEqual(SignInState.Busy, _model.State);
            Assert.AreEqual("Signing in...", _model.Message);
            _http.Complete(1, LoginOk("second-pilot"));
            Assert.AreEqual("second-pilot", PlatformSession.Username);
        }

        [Test]
        public void ClosingScreenPreventsLateLoginFromChangingTheSession()
        {
            // frob:tests Assets/Scripts/Hullbreach.Platform/SignInModel.cs::SignInModel.CancelPending kind="unit"
            _http.Hold = true;
            _model.Submit("pilot", Password);
            _model.CancelPending();
            _http.Complete(0, LoginOk("pilot"));

            Assert.AreEqual(SignInState.Editing, _model.State);
            Assert.IsFalse(PlatformSession.HasChosen);
        }

        [Test]
        public void LeavingOfflineReturnsToAnEmptyForm()
        {
            _model.PlayOffline();
            _model.SignOut();

            Assert.AreEqual(SignInState.Editing, _model.State);
            Assert.AreEqual(string.Empty, _model.Message);
            Assert.IsFalse(PlatformSession.IsOffline);
        }

        [Test]
        public void SignOutWhileEditingDoesNothing()
        {
            _model.SignOut();
            Assert.AreEqual(0, _seen.Count);
        }

        [Test]
        public void NewModelResumesTheChoiceMadeEarlierThisRun()
        {
            _http.Enqueue(LoginOk("pilot"));
            _model.Submit("pilot", Password);
            Assert.AreEqual(SignInState.SignedIn, NewModel().State);

            PlatformSession.PlayOffline();
            SignInModel offline = NewModel();
            Assert.AreEqual(SignInState.Offline, offline.State);
            Assert.AreEqual("Playing offline.", offline.Message);
        }

        [Test]
        public void PasswordAndTokenNeverReachLogsOrMessages()
        {
            _http.Enqueue(PlatformHttpResponse.Http(401, "{}"));
            _model.Submit("pilot", Password);
            _http.Enqueue(LoginOk("pilot"));
            _model.Submit("pilot", Password);
            _model.SignOut();

            StringAssert.DoesNotContain(Password, _log.All);
            StringAssert.DoesNotContain(Token, _log.All);
            StringAssert.DoesNotContain(Token, _model.Message);
        }

        [Test]
        public void MessageForAddsHintOnlyWhenUnreachable()
        {
            var unreachable = new LoginOutcome(LoginStatus.Unreachable, null, "p", "down.");
            var invalid = new LoginOutcome(LoginStatus.InvalidCredentials, null, "p", "nope.");
            Assert.AreEqual("down." + SignInModel.UnreachableHint, SignInModel.MessageFor(unreachable));
            Assert.AreEqual("nope.", SignInModel.MessageFor(invalid));
        }

        [Test]
        public void NullClientIsAProgrammerError()
        {
            Assert.Throws<System.ArgumentNullException>(() => new SignInModel(null));
        }
    }
}
