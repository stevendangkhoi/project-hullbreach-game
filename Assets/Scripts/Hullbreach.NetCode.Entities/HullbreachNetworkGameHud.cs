using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Hullbreach.NetCode.Entities
{
    /// <summary>Small editable uGUI diagnostic/status panel for the network gameplay scene.</summary>
    [AddComponentMenu("Hullbreach/Online/Network Game HUD")]
    public sealed class HullbreachNetworkGameHud : MonoBehaviour
    {
        [SerializeField] TMP_Text statusText;
        [SerializeField] Button leaveButton;
        bool _wasConnected;

        void OnEnable()
        {
            _wasConnected = false;
            leaveButton.onClick.AddListener(Leave);
        }
        void OnDisable() => leaveButton.onClick.RemoveListener(Leave);

        void Update()
        {
            var replica = HullbreachNetCodeClient.Replica;
            int shipCount = replica?.Ships.Count ?? 0;
            string mode = HullbreachNetworkGameplayController.IsBuildMode ? "BUILD" : "FLIGHT";
            string controls = HullbreachNetworkGameplayController.IsBuildMode
                ? $"1-7 select • click place • right-click remove • Tab fly\n{HullbreachNetworkGameplayController.BuildStatus}"
                : "W/S thrust • A/D steer • Space/click fire • Tab build";
            if (HullbreachNetCodeClient.IsConnected)
            {
                _wasConnected = true;
                statusText.text = $"Connected as #{HullbreachNetCodeClient.LocalNetworkId}  •  Replicated ships: {shipCount}  •  {mode}\n{HullbreachPlatformAdmission.StatusLine}\n{controls}";
            }
            else if (HullbreachPlatformAdmission.HasVerdict && !HullbreachPlatformAdmission.Admitted)
                statusText.text = HullbreachPlatformAdmission.StatusLine + "\nUse Leave to exit this match.";
            else
                statusText.text = _wasConnected
                    ? "Disconnected from the server. Use Leave to exit this match."
                    : "Waiting for Netcode connection...";
        }

        async void Leave()
        {
            leaveButton.interactable = false;
            // A direct/LAN match has no lobby to go back to, so Leave closes the game.
            if (HullbreachDirectLaunch.Plan.IsDirect)
            {
                Debug.Log("Hullbreach direct: leaving the match closes the game.");
                Application.Quit();
                return;
            }
            try
            {
                await HullbreachLobbyService.Instance.LeaveAndReturnToLobbyAsync();
            }
            catch (Exception exception)
            {
                leaveButton.interactable = true;
                statusText.text = exception.Message;
                Debug.LogException(exception);
            }
        }
    }
}
