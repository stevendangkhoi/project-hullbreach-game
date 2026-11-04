using System;

namespace Hullbreach.Platform
{
    // Where the sign-in screen is; the view shows the form only in Editing and Busy.
    // frob:doc docs/platform-link.md#sign-in-screen
    public enum SignInState
    {
        Editing,
        Busy,
        SignedIn,
        Offline,
    }

    // Engine-free state behind the sign-in screen (S07-2) so every branch is unit-tested;
    // the uGUI view only copies these properties onto widgets and forwards clicks.
    // frob:doc docs/platform-link.md#sign-in-screen
    public sealed class SignInModel
    {
        // frob:doc docs/platform-link.md#sign-in-screen
        public const string UnreachableHint = " You can still play offline.";

        readonly PlatformClient _client;
        readonly IPlatformLog _log;
        readonly SessionTokenStore _store;

        // Bumped by every submit and by leaving Busy, so a late login answer is dropped.
        int _attempt;

        // frob:doc docs/platform-link.md#sign-in-screen
        public SignInModel(PlatformClient client, IPlatformLog log = null, SessionTokenStore store = null)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _log = log ?? NullPlatformLog.Instance;
            _store = store;
            // Returning to the lobby later in the run must not ask again.
            if (PlatformSession.IsSignedIn)
            {
                State = SignInState.SignedIn;
                Message = "Signed in as " + PlatformSession.Username + ".";
            }
            else if (PlatformSession.IsOffline)
            {
                State = SignInState.Offline;
                Message = "Playing offline.";
            }
            else
            {
                State = SignInState.Editing;
                Message = string.Empty;
            }
        }

        // frob:doc docs/platform-link.md#sign-in-screen
        public event Action Changed;

        // frob:doc docs/platform-link.md#sign-in-screen
        public SignInState State { get; private set; }

        // Text for the message line; never contains the password or token.
        // frob:doc docs/platform-link.md#sign-in-screen
        public string Message { get; private set; }

        // frob:doc docs/platform-link.md#sign-in-screen
        public bool IsError { get; private set; }

        // Status of the last finished attempt, or null before any; lets the view style per outcome.
        // frob:doc docs/platform-link.md#sign-in-screen
        public LoginStatus? LastStatus { get; private set; }

        // frob:doc docs/platform-link.md#sign-in-screen
        public bool CanSubmit => State == SignInState.Editing;

        // frob:doc docs/platform-link.md#sign-in-screen
        public bool IsDone => State == SignInState.SignedIn || State == SignInState.Offline;

        // One line for the account bar the lobby shows once the screen is answered.
        // frob:doc docs/platform-link.md#sign-in-screen
        public string AccountLine =>
            State == SignInState.SignedIn ? "Signed in as " + PlatformSession.Username :
            State == SignInState.Offline ? "Playing offline" : string.Empty;

        // S07-3: reuse the token remembered from a previous launch if the platform still accepts it.
        // frob:doc docs/platform-link.md#remembered-session
        public void TryRestore()
        {
            if (_store == null || State != SignInState.Editing) return;
            if (!_store.TryLoad(out _, out string token)) return;
            int attempt = ++_attempt;
            Set(SignInState.Busy, "Restoring your session...", isError: false);
            _client.RestoreSession(token, outcome => OnRestore(attempt, outcome));
        }

        // frob:doc docs/platform-link.md#sign-in-screen
        public void Submit(string username, string password)
        {
            if (!CanSubmit)
            {
                _log.Info($"Hullbreach sign-in: ignoring submit while {State}.");
                return;
            }
            int attempt = ++_attempt;
            Set(SignInState.Busy, "Signing in...", isError: false);
            _client.Login(username, password, outcome => OnLogin(attempt, outcome));
        }

        // Reads the password, blanks the field before sending, then submits (S07-2 criterion 3).
        // frob:ticket 01M3DG5Y1903PCF86DJBDSPXJD
        // frob:doc docs/platform-link.md#sign-in-screen
        public void SubmitClearing(string username, Func<string> readPassword, Action<string> writePassword)
        {
            string password = readPassword();
            writePassword(string.Empty);
            Submit(username, password);
        }

        // Allowed while Busy too, so a slow or dead platform never traps the player.
        // frob:doc docs/platform-link.md#sign-in-screen
        public void PlayOffline()
        {
            if (IsDone) return;
            _attempt++;
            PlatformSession.PlayOffline();
            _log.Info("Hullbreach sign-in: player chose to play offline.");
            Set(SignInState.Offline, "Playing offline.", isError: false);
        }

        // Back to the form from the account bar; forgets the in-memory token.
        // frob:doc docs/platform-link.md#sign-in-screen
        public void SignOut()
        {
            if (!IsDone) return;
            int attempt = ++_attempt;
            bool wasSignedIn = State == SignInState.SignedIn;
            string token = PlatformSession.Token;
            PlatformSession.SignOut();
            _store?.Clear();
            _log.Info(wasSignedIn
                ? "Hullbreach sign-in: signed out; the session token was discarded."
                : "Hullbreach sign-in: leaving offline mode to sign in.");
            LastStatus = null;
            Set(SignInState.Editing, wasSignedIn ? "Signed out." : string.Empty, isError: false);
            if (wasSignedIn)
            {
                _client.Logout(token, revoked =>
                {
                    // a late sign-out answer must not change a newer sign-in attempt.
                    if (!revoked && attempt == _attempt && State == SignInState.Editing)
                        Set(SignInState.Editing,
                            "Signed out on this device. The platform could not revoke the session; it will expire normally.",
                            isError: true);
                });
            }
        }

        // stop a closed screen from signing the player in after its request finishes.
        // frob:doc docs/platform-link.md#sign-in-screen
        public void CancelPending()
        {
            _attempt++;
            if (State == SignInState.Busy) Set(SignInState.Editing, string.Empty, isError: false);
        }

        // frob:doc docs/platform-link.md#sign-in-screen
        public static string MessageFor(LoginOutcome outcome) =>
            outcome.Status == LoginStatus.Unreachable ? outcome.Message + UnreachableHint : outcome.Message;

        void OnLogin(int attempt, LoginOutcome outcome)
        {
            if (attempt != _attempt || State != SignInState.Busy)
            {
                _log.Info($"Hullbreach sign-in: dropping a late {outcome.Status} answer; the player moved on.");
                return;
            }
            LastStatus = outcome.Status;
            if (outcome.IsSuccess)
            {
                PlatformSession.SignIn(outcome.Username, outcome.Token);
                _store?.Save(outcome.Username, outcome.Token);
                Set(SignInState.SignedIn, "Signed in as " + outcome.Username + ".", isError: false);
                return;
            }
            Set(SignInState.Editing, MessageFor(outcome), isError: true);
        }

        void OnRestore(int attempt, LoginOutcome outcome)
        {
            if (attempt != _attempt || State != SignInState.Busy)
            {
                _log.Info($"Hullbreach sign-in: dropping a late {outcome.Status} session check; the player moved on.");
                return;
            }
            if (outcome.IsSuccess)
            {
                PlatformSession.SignIn(outcome.Username, outcome.Token);
                _store.Save(outcome.Username, outcome.Token);
                _log.Info($"Hullbreach sign-in: restored the remembered session for '{outcome.Username}'.");
                Set(SignInState.SignedIn, "Signed in as " + outcome.Username + ".", isError: false);
                return;
            }
            // only a 401 proves the token is dead; keep it through temporary failures.
            bool expired = outcome.Status == LoginStatus.InvalidCredentials;
            if (expired) _store.Clear();
            _log.Info($"Hullbreach sign-in: remembered session not restored ({outcome.Status}).");
            Set(SignInState.Editing, outcome.Message, isError: true);
        }

        void Set(SignInState state, string message, bool isError)
        {
            State = state;
            Message = message;
            IsError = isError;
            Changed?.Invoke();
        }
    }
}
