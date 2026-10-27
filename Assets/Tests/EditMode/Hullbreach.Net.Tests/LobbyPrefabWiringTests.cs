#if UNITY_EDITOR
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hullbreach.Net.Tests
{
    public sealed class LobbyPrefabWiringTests
    {
        const string PrefabPath = "Assets/Prefabs/UI/LobbyCanvas.prefab";
        const string LanPath = "Background/MainPanel/BrowserRoot/PublicLobbies/LanControls";

        [TestCase("lanHostButton", "UnityEngine.UI.Button", "LanHostButton")]
        [TestCase("lanAddressInput", "TMPro.TMP_InputField", "LanAddressInput")]
        [TestCase("lanJoinButton", "UnityEngine.UI.Button", "LanJoinButton")]
        public void LobbyPrefabHasWiredLanControl(string field, string type, string objectName)
        {
            // the old prefab had working code but no controls assigned to these fields.
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab);
            Transform controls = prefab.transform.Find(LanPath);
            Assert.IsNotNull(controls, "the lan controls must be in the public lobby column");
            Component view = prefab.GetComponents<Component>().Single(component =>
                component != null && component.GetType().Name == "HullbreachLobbyView");
            var serialized = new SerializedObject(view);
            SerializedProperty property = serialized.FindProperty(field);
            Assert.IsNotNull(property);
            var control = property.objectReferenceValue as Component;
            Assert.IsNotNull(control, field + " is not wired");
            Assert.AreEqual(type, control.GetType().FullName);
            Assert.AreEqual(objectName, control.name);
            Assert.AreSame(controls, control.transform.parent);
            Assert.IsTrue(control.gameObject.activeSelf);
            Assert.AreEqual(1, controls.Cast<Transform>().Count(child => child.name == objectName));
        }

        [Test]
        public void LobbyPrefabKeepsRelayAndRoomBindings()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab);
            Component view = prefab.GetComponents<Component>().Single(component =>
                component != null && component.GetType().Name == "HullbreachLobbyView");
            var serialized = new SerializedObject(view);
            foreach (string field in new[] { "hostButton", "joinCodeButton", "refreshButton", "startGameButton", "leaveButton" })
                Assert.IsNotNull(serialized.FindProperty(field).objectReferenceValue, field + " was disconnected");
        }
    }
}
#endif
