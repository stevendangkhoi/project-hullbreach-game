using Hullbreach.Platform;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Hullbreach.NetCode.Entities
{
    // Inspector-wired uGUI sign-in screen over the lobby (S07-2); all logic is in SignInModel.
    // Widgets live in SignInCanvas.prefab so they can be restyled without code changes.
    // frob:doc docs/platform-link.md#sign-in-screen
    [AddComponentMenu("Hullbreach/Online/Sign-In View")]
    public sealed class HullbreachSignInView : MonoBehaviour
    {
        [Header("Sign-in form (covers the lobby until answered)")]
        [SerializeField] GameObject formRoot;
        [SerializeField] TMP_InputField usernameInput;
        [SerializeField] TMP_InputField passwordInput;
        [SerializeField] Button signInButton;
        [SerializeField] Button playOfflineButton;
        [SerializeField] TMP_Text messageText;
        [SerializeField] Color messageColor = new Color(0.9f, 0.95f, 1f);
        [SerializeField] Color errorColor = new Color(1f, 0.42f, 0.4f);

        [Header("Account bar (shown over the lobby afterwards)")]
        [SerializeField] GameObject accountRoot;
        [SerializeField] TMP_Text accountText;
        [SerializeField] Button signOutButton;
        [SerializeField] TMP_Text signOutLabel;

        SignInModel _model;

        // Tab / Shift+Tab / arrows walk username, password, sign in, play offline.
        // frob:ticket 01M5XZJRNREX0M9DF1M6PE7G7V
        void WireTabOrder()
        {
            HullbreachTabNavigation tabs = GetComponent<HullbreachTabNavigation>();
            if (tabs == null)
            {
                tabs = gameObject.AddComponent<HullbreachTabNavigation>();
            }

            tabs.SetOrder(HullbreachPlatformRuntime.Log, usernameInput, passwordInput, signInButton, playOfflineButton);
        }

        void OnEnable()
        {
            _model = new SignInModel(HullbreachPlatformRuntime.Client, HullbreachPlatformRuntime.Log,
                HullbreachPlatformRuntime.TokenStore);
            _model.Changed += Refresh;
            signInButton.onClick.AddListener(Submit);
            passwordInput.onSubmit.AddListener(SubmitFromField);
            playOfflineButton.onClick.AddListener(_model.PlayOffline);
            signOutButton.onClick.AddListener(_model.SignOut);
            WireTabOrder();
            Refresh();
            _model.TryRestore();
        }

        void OnDisable()
        {
            _model.Changed -= Refresh;
            _model.CancelPending();
            signInButton.onClick.RemoveListener(Submit);
            passwordInput.onSubmit.RemoveListener(SubmitFromField);
            playOfflineButton.onClick.RemoveListener(_model.PlayOffline);
            signOutButton.onClick.RemoveListener(_model.SignOut);
        }

        void SubmitFromField(string _) => Submit();

        // The model blanks the password field as it sends, so the clearing is unit-tested.
        // frob:ticket 01M3DG5Y1903PCF86DJBDSPXJD
        void Submit() =>
            _model.SubmitClearing(usernameInput.text, () => passwordInput.text, v => passwordInput.text = v);

        void Refresh()
        {
            bool done = _model.IsDone;
            formRoot.SetActive(!done);
            accountRoot.SetActive(done);

            bool editable = _model.CanSubmit;
            usernameInput.interactable = editable;
            passwordInput.interactable = editable;
            signInButton.interactable = editable;
            messageText.text = _model.Message;
            messageText.color = _model.IsError ? errorColor : messageColor;

            accountText.text = _model.AccountLine;
            signOutLabel.text = _model.State == SignInState.SignedIn ? "SIGN OUT" : "SIGN IN";
        }
    }
}
