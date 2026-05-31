using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using Axiom.Editor.AgentBridge.Core;

#if AXIOM_HAS_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Controls;
#endif

namespace Axiom.Editor.AgentBridge.Actions
{
    /// <summary>
    /// Gives the agent the ability to simulate input events (keyboard, mouse, gamepad) in Play Mode.
    /// Requires com.unity.inputsystem package for full functionality.
    /// GetInputSystemInfo() works in both Edit and Play Mode.
    /// All other operations require Play Mode.
    /// </summary>
    public static class InputSimulationActions
    {
        // ─────────────────────────────────────────────────────
        //  1.1 SimulateKeyPress
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Simulates a keyboard key press and release. Play Mode only.
        /// </summary>
        /// <param name="keyName">Key name (e.g., "W", "Space", "LeftShift", "Escape").</param>
        /// <param name="holdDurationFrames">Number of frames to hold the key. 1 = single tap.</param>
        public static ActionResult SimulateKeyPress(string keyName, int holdDurationFrames = 1)
        {
            if (!EditorApplication.isPlaying)
                return ActionResult.Fail("SimulateKeyPress requires Play Mode. Call PlayModeActions.EnterPlayMode() first.");

#if AXIOM_HAS_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return ActionResult.Fail("No keyboard device found.");

            // Parse keyName to Key enum
            if (!Enum.TryParse<Key>(keyName, true, out Key key))
                return ActionResult.Fail($"Unknown key: {keyName}. Use Key enum names (e.g., Space, W, LeftShift, Escape).");

            // Queue key down event
            using (StateEvent.From(keyboard, out var eventPtr))
            {
                keyboard[key].WriteValueIntoEvent(1f, eventPtr);
                InputSystem.QueueEvent(eventPtr);
            }
            InputSystem.Update();

            // Hold for specified duration
            if (holdDurationFrames > 1)
            {
                for (int i = 1; i < holdDurationFrames; i++)
                {
                    InputSystem.Update();
                }
            }

            // Queue key up event
            using (StateEvent.From(keyboard, out var eventPtr))
            {
                keyboard[key].WriteValueIntoEvent(0f, eventPtr);
                InputSystem.QueueEvent(eventPtr);
            }
            InputSystem.Update();

            return ActionResult.Ok($"Simulated key press: {keyName} (held for {holdDurationFrames} frame(s))");
#else
            return ActionResult.Fail("Input System package not installed. SimulateKeyPress requires com.unity.inputsystem.");
#endif
        }

        // ─────────────────────────────────────────────────────
        //  1.2 SimulateMouseClick
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Simulates a mouse click at screen coordinates. Play Mode only.
        /// </summary>
        /// <param name="screenX">X coordinate in screen pixels.</param>
        /// <param name="screenY">Y coordinate in screen pixels.</param>
        /// <param name="button">0 = left, 1 = right, 2 = middle.</param>
        public static ActionResult SimulateMouseClick(float screenX, float screenY, int button = 0)
        {
            if (!EditorApplication.isPlaying)
                return ActionResult.Fail("SimulateMouseClick requires Play Mode. Call PlayModeActions.EnterPlayMode() first.");

#if AXIOM_HAS_INPUT_SYSTEM
            var mouse = Mouse.current;
            if (mouse == null)
                return ActionResult.Fail("No mouse device found.");

            // Move to target position
            InputState.Change(mouse.position, new Vector2(screenX, screenY));
            InputSystem.Update();

            // Click the button
            ButtonControl btn = button switch
            {
                0 => mouse.leftButton,
                1 => mouse.rightButton,
                2 => mouse.middleButton,
                _ => null
            };

            if (btn == null)
                return ActionResult.Fail($"Invalid button index: {button}. Use 0 (left), 1 (right), or 2 (middle).");

            // Press
            InputState.Change(btn, 1f);
            InputSystem.Update();

            // Release
            InputState.Change(btn, 0f);
            InputSystem.Update();

            return ActionResult.Ok($"Simulated mouse click at ({screenX}, {screenY}), button {button}");
#else
            return ActionResult.Fail("Input System package not installed. SimulateMouseClick requires com.unity.inputsystem.");
#endif
        }

        // ─────────────────────────────────────────────────────
        //  1.3 SimulateMouseMove
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Moves the mouse cursor to screen coordinates. Play Mode only.
        /// </summary>
        /// <param name="screenX">X coordinate.</param>
        /// <param name="screenY">Y coordinate.</param>
        public static ActionResult SimulateMouseMove(float screenX, float screenY)
        {
            if (!EditorApplication.isPlaying)
                return ActionResult.Fail("SimulateMouseMove requires Play Mode. Call PlayModeActions.EnterPlayMode() first.");

#if AXIOM_HAS_INPUT_SYSTEM
            var mouse = Mouse.current;
            if (mouse == null)
                return ActionResult.Fail("No mouse device found.");

            InputState.Change(mouse.position, new Vector2(screenX, screenY));
            InputSystem.Update();

            return ActionResult.Ok($"Moved mouse to ({screenX}, {screenY})");
#else
            return ActionResult.Fail("Input System package not installed. SimulateMouseMove requires com.unity.inputsystem.");
#endif
        }

        // ─────────────────────────────────────────────────────
        //  1.4 SimulateMouseDrag
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Simulates a mouse drag from one point to another. Play Mode only.
        /// </summary>
        /// <param name="fromX">Start X.</param>
        /// <param name="fromY">Start Y.</param>
        /// <param name="toX">End X.</param>
        /// <param name="toY">End Y.</param>
        /// <param name="steps">Number of intermediate positions for smooth dragging.</param>
        /// <param name="button">0 = left, 1 = right, 2 = middle.</param>
        public static ActionResult SimulateMouseDrag(float fromX, float fromY, float toX, float toY, int steps = 10, int button = 0)
        {
            if (!EditorApplication.isPlaying)
                return ActionResult.Fail("SimulateMouseDrag requires Play Mode. Call PlayModeActions.EnterPlayMode() first.");

#if AXIOM_HAS_INPUT_SYSTEM
            var mouse = Mouse.current;
            if (mouse == null)
                return ActionResult.Fail("No mouse device found.");

            ButtonControl btn = button switch
            {
                0 => mouse.leftButton,
                1 => mouse.rightButton,
                2 => mouse.middleButton,
                _ => null
            };

            if (btn == null)
                return ActionResult.Fail($"Invalid button index: {button}. Use 0 (left), 1 (right), or 2 (middle).");

            // Move to start position
            InputState.Change(mouse.position, new Vector2(fromX, fromY));
            InputSystem.Update();

            // Press button
            InputState.Change(btn, 1f);
            InputSystem.Update();

            // Interpolate through steps
            for (int i = 1; i <= steps; i++)
            {
                float t = i / (float)steps;
                float x = Mathf.Lerp(fromX, toX, t);
                float y = Mathf.Lerp(fromY, toY, t);
                InputState.Change(mouse.position, new Vector2(x, y));
                InputSystem.Update();
            }

            // Release button
            InputState.Change(btn, 0f);
            InputSystem.Update();

            return ActionResult.Ok($"Simulated mouse drag from ({fromX}, {fromY}) to ({toX}, {toY}) in {steps} steps");
#else
            return ActionResult.Fail("Input System package not installed. SimulateMouseDrag requires com.unity.inputsystem.");
#endif
        }

        // ─────────────────────────────────────────────────────
        //  1.5 SimulateGamepadInput
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Simulates gamepad stick or button input. Play Mode only.
        /// </summary>
        /// <param name="controlPath">Input control path (e.g., "leftStick", "buttonSouth", "leftTrigger").</param>
        /// <param name="value">Value to set. Sticks: Vector2 as "x,y". Buttons/triggers: float 0-1.</param>
        public static ActionResult SimulateGamepadInput(string controlPath, string value)
        {
            if (!EditorApplication.isPlaying)
                return ActionResult.Fail("SimulateGamepadInput requires Play Mode. Call PlayModeActions.EnterPlayMode() first.");

#if AXIOM_HAS_INPUT_SYSTEM
            var gamepad = Gamepad.current;
            if (gamepad == null)
            {
                // Create a virtual gamepad for simulation
                gamepad = InputSystem.AddDevice<Gamepad>();
                if (gamepad == null)
                    return ActionResult.Fail("Failed to create virtual gamepad.");
            }

            var control = gamepad.TryGetChildControl(controlPath);
            if (control == null)
                return ActionResult.Fail($"Unknown gamepad control: {controlPath}. Use paths like 'leftStick', 'buttonSouth', 'leftTrigger'.");

            // Parse and set value based on control type
            try
            {
                if (control is StickControl stick)
                {
                    var parts = value.Split(',');
                    if (parts.Length != 2)
                        return ActionResult.Fail($"Stick value must be 'x,y' format. Got: {value}");

                    var vec = new Vector2(float.Parse(parts[0]), float.Parse(parts[1]));
                    InputState.Change(stick, vec);
                }
                else if (control is ButtonControl btn)
                {
                    InputState.Change(btn, float.Parse(value));
                }
                else if (control is AxisControl axis)
                {
                    InputState.Change(axis, float.Parse(value));
                }
                else
                {
                    return ActionResult.Fail($"Unsupported control type for {controlPath}: {control.GetType().Name}");
                }

                InputSystem.Update();
                return ActionResult.Ok($"Simulated gamepad input: {controlPath} = {value}");
            }
            catch (Exception ex)
            {
                return ActionResult.Fail($"Failed to parse value '{value}': {ex.Message}");
            }
#else
            return ActionResult.Fail("Input System package not installed. SimulateGamepadInput requires com.unity.inputsystem.");
#endif
        }

        // ─────────────────────────────────────────────────────
        //  1.6 SimulateInputAction
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Triggers a named Input Action directly, bypassing device simulation.
        /// Useful when the project uses Input Action assets.
        /// </summary>
        /// <param name="actionMapName">Action Map name (e.g., "Player").</param>
        /// <param name="actionName">Action name (e.g., "Jump", "Move").</param>
        /// <param name="value">Value string — "true" for button, "x,y" for Vector2, "x,y,z" for Vector3.</param>
        public static ActionResult SimulateInputAction(string actionMapName, string actionName, string value = "true")
        {
            if (!EditorApplication.isPlaying)
                return ActionResult.Fail("SimulateInputAction requires Play Mode. Call PlayModeActions.EnterPlayMode() first.");

#if AXIOM_HAS_INPUT_SYSTEM
            // Find all PlayerInput components in scene
            var playerInputs = UnityEngine.Object.FindObjectsByType<UnityEngine.InputSystem.PlayerInput>(FindObjectsSortMode.None);

            if (playerInputs.Length == 0)
                return ActionResult.Fail("No PlayerInput components found in scene. SimulateInputAction requires PlayerInput to be present.");

            foreach (var playerInput in playerInputs)
            {
                var actionMap = playerInput.actions?.FindActionMap(actionMapName);
                if (actionMap == null)
                    continue;

                var action = actionMap.FindAction(actionName);
                if (action == null)
                    continue;

                // Trigger the action by simulating a callback
                // This is a high-level approach that doesn't require device simulation
                // Note: This won't work for all action types (e.g., hold, multi-tap)
                // For those, use the device simulation methods instead

                return ActionResult.Fail($"Found action {actionMapName}/{actionName}, but direct action triggering is not fully implemented. Use device simulation methods (SimulateKeyPress, SimulateGamepadInput) instead.");
            }

            return ActionResult.Fail($"Action {actionMapName}/{actionName} not found in any PlayerInput component.");
#else
            return ActionResult.Fail("Input System package not installed. SimulateInputAction requires com.unity.inputsystem.");
#endif
        }

        // ─────────────────────────────────────────────────────
        //  1.7 GetInputSystemInfo
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Reports the current input configuration — which system is active, connected devices,
        /// and available Input Action assets.
        /// Works in both Edit and Play Mode.
        /// </summary>
        public static ActionResult GetInputSystemInfo()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Input System Info\n");

#if AXIOM_HAS_INPUT_SYSTEM
            // Backend
            sb.AppendLine("## Backend\n");
            sb.AppendLine($"- Active: New Input System (com.unity.inputsystem {UnityEngine.InputSystem.InputSystem.version})");
            
            // Check if legacy input is also enabled
            bool legacyEnabled = false;
            #if ENABLE_LEGACY_INPUT_MANAGER
            legacyEnabled = true;
            #endif
            sb.AppendLine($"- Legacy Input Manager: {(legacyEnabled ? "Enabled (Both)" : "Disabled")}");
            sb.AppendLine();

            // Connected Devices
            sb.AppendLine("## Connected Devices\n");
            var devices = InputSystem.devices;
            if (devices.Count == 0)
            {
                sb.AppendLine("*No devices connected.*\n");
            }
            else
            {
                sb.AppendLine("| Device | Type | ID |");
                sb.AppendLine("| :--- | :--- | :--- |");
                foreach (var device in devices)
                {
                    sb.AppendLine($"| {device.displayName} | {device.GetType().Name} | {device.deviceId} |");
                }
                sb.AppendLine();
            }

            // Input Action Assets
            string[] actionAssetGuids = AssetDatabase.FindAssets("t:InputActionAsset");
            sb.AppendLine($"## Input Action Assets ({actionAssetGuids.Length})\n");

            if (actionAssetGuids.Length == 0)
            {
                sb.AppendLine("*No Input Action assets found in project.*\n");
            }
            else
            {
                sb.AppendLine("| Asset | Action Maps | Actions |");
                sb.AppendLine("| :--- | :--- | :--- |");

                foreach (string guid in actionAssetGuids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.InputSystem.InputActionAsset>(path);
                    if (asset == null) continue;

                    var mapNames = new List<string>();
                    var actionNames = new List<string>();

                    foreach (var map in asset.actionMaps)
                    {
                        mapNames.Add(map.name);
                        foreach (var action in map.actions)
                        {
                            actionNames.Add(action.name);
                        }
                    }

                    sb.AppendLine($"| {path} | {string.Join(", ", mapNames)} | {string.Join(", ", actionNames)} |");
                }
                sb.AppendLine();
            }

            // PlayerInput Components in Scene (Play Mode or Edit Mode)
            if (EditorApplication.isPlaying)
            {
                var playerInputs = UnityEngine.Object.FindObjectsByType<UnityEngine.InputSystem.PlayerInput>(FindObjectsSortMode.None);
                sb.AppendLine($"## PlayerInput Components in Scene ({playerInputs.Length})\n");

                if (playerInputs.Length == 0)
                {
                    sb.AppendLine("*No PlayerInput components in scene.*\n");
                }
                else
                {
                    sb.AppendLine("| Object | Action Asset | Default Map | Behavior |");
                    sb.AppendLine("| :--- | :--- | :--- | :--- |");

                    foreach (var pi in playerInputs)
                    {
                        string assetName = pi.actions != null ? pi.actions.name : "<none>";
                        string defaultMap = pi.defaultActionMap ?? "<none>";
                        string behavior = pi.notificationBehavior.ToString();
                        sb.AppendLine($"| {pi.gameObject.name} | {assetName} | {defaultMap} | {behavior} |");
                    }
                    sb.AppendLine();
                }
            }
            else
            {
                sb.AppendLine("## PlayerInput Components\n");
                sb.AppendLine("*(Enter Play Mode to see active PlayerInput components.)*\n");
            }

#else
            sb.AppendLine("## Backend\n");
            sb.AppendLine("- Active: Legacy Input Manager (com.unity.inputsystem not installed)");
            sb.AppendLine("- New Input System: Not Available");
            sb.AppendLine();
            sb.AppendLine("**Note:** Input simulation features require the New Input System package.");
            sb.AppendLine("Install via Package Manager: `Window > Package Manager > Input System`");
            sb.AppendLine();
#endif

            string reportPath = OutputWriter.WriteReport("input_system_info", sb.ToString());
            return ActionResult.Ok($"Input System info written to: {reportPath}");
        }

        // ─────────────────────────────────────────────────────
        //  Menu Items
        // ─────────────────────────────────────────────────────

        [MenuItem("Axiom/AgentBridge/Actions/Input System Info")]
        public static void MenuGetInfo() => GetInputSystemInfo();
    }
}
