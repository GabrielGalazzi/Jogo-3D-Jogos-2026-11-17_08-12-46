using MaidCafe.Audio;
using MaidCafe.Core;
using MaidCafe.Generation;
using MaidCafe.Interaction;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MaidCafe.Player
{
    /// <summary>
    /// Builds the complete first-person player hierarchy in code:
    /// Player (CharacterController + logic) → Head → Camera (+ phone model, night-vision light).
    /// </summary>
    public static class PlayerFactory
    {
        public static PlayerContext Create(Vector3 position, float yaw)
        {
            var root = new GameObject("Player");
            root.SetActive(false); // defer Awake until everything is wired
            root.tag = "Player";
            root.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));

            root.AddComponent<CharacterController>();
            var motor = root.AddComponent<PlayerMotor>();
            var look = root.AddComponent<PlayerLook>();
            root.AddComponent<Interactor>();
            var phone = root.AddComponent<PhoneController>();
            root.AddComponent<PlayerContext>();
            root.AddComponent<BreathController>();
            root.AddComponent<PlayerAudio>();

            var head = new GameObject("Head").transform;
            head.SetParent(root.transform, false);
            head.localPosition = new Vector3(0f, 1.63f, 0f);

            var camGo = new GameObject("PlayerCamera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(head, false);
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.04f;
            cam.farClipPlane = 80f;
            cam.fieldOfView = GameSettings.FieldOfView;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<RaytracedListener>(); // the player's ear in the ray-traced audio world
            var camData = cam.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.FastApproximateAntialiasing;

            motor.SetHead(head);
            look.SetReferences(head, cam);
            phone.SetPhoneModel(BuildPhoneModel(camGo.transform));

            root.SetActive(true);
            return root.GetComponent<PlayerContext>();
        }

        private static Transform BuildPhoneModel(Transform cameraTransform)
        {
            var phone = new GameObject("Phone").transform;
            phone.SetParent(cameraTransform, false);
            phone.localPosition = new Vector3(0.16f, -0.45f, 0.3f);
            phone.localRotation = Quaternion.Euler(-12f, -18f, -6f);

            var body = FloorBuilder.LocalBox(phone, "Body", Vector3.zero, new Vector3(0.075f, 0.15f, 0.009f),
                MaterialLibrary.Get("PhoneBody", new Color(0.95f, 0.75f, 0.82f), 0.6f), collider: false);
            var screen = FloorBuilder.LocalBox(phone, "Screen", new Vector3(0f, 0f, -0.005f), new Vector3(0.066f, 0.135f, 0.001f),
                MaterialLibrary.Get("PhoneScreen", Color.black, 0.9f, new Color(0.1f, 0.5f, 0.15f)), collider: false);
            foreach (var r in new[] { body.GetComponent<MeshRenderer>(), screen.GetComponent<MeshRenderer>() })
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            return phone;
        }
    }
}
