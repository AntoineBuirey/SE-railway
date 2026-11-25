using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        private const string CHANNEL = "SER_DOORS_COMMAND";
        private const string LEFT_DOOR_TAG = "[DOOR_LEFT]";
        private const string RIGHT_DOOR_TAG = "[DOOR_RIGHT]";
        private const double MIN_ALIGNMENT = 0.5;

        private readonly IMyBroadcastListener CommandListener;

        private readonly List<IMyDoor> LeftDoors = new List<IMyDoor>();
        private readonly List<IMyDoor> RightDoors = new List<IMyDoor>();
        private bool DoorTransitionPending;

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update100;

            CommandListener = IGC.RegisterBroadcastListener(CHANNEL);
            CommandListener.SetMessageCallback("doors-command");

            Me.CustomData = "";
        }

        public void Main(string argument, UpdateType updateSource)
        {
            while (CommandListener.HasPendingMessage)
            {
                var message = CommandListener.AcceptMessage();
                if (message.Tag != CHANNEL)
                {
                    continue;
                }

                string payload = message.Data.ToString();
                if (payload == null)
                {
                    CloseTaggedDoors();
                    Echo("Invalid door command payload; tagged doors closed.");
                    continue;
                }

                Vector3D trainForward;
                Vector3D trainLeft;
                bool openTrainLeft;
                bool openTrainRight;
                string errorMessage;
                if (!TryParseCommand(payload, out trainForward, out trainLeft,
                    out openTrainLeft, out openTrainRight, out errorMessage))
                {
                    CloseTaggedDoors();
                    Echo("Invalid door command; tagged doors closed.");
                    Echo("Payload: " + payload);
                    Echo("Error: " + errorMessage);
                    Me.CustomData = Me.CustomData + "\n" + errorMessage;
                    continue;
                }

                ApplyDoorCommand(trainForward, trainLeft, openTrainLeft, openTrainRight);
            }

            UpdateDoorAvailability();
            Echo("Listening for connected locomotive.");
        }

        private void ApplyDoorCommand(
            Vector3D trainForward,
            Vector3D trainLeft,
            bool openTrainLeft,
            bool openTrainRight)
        {
            RefreshDoors();

            if (!openTrainLeft && !openTrainRight)
            {
                CloseTaggedDoors();
                Echo("Tagged doors closed.");
                return;
            }

            if (openTrainLeft && openTrainRight)
            {
                SetDoors(LeftDoors, true);
                SetDoors(RightDoors, true);
                Echo("Both sides opened.");
                return;
            }

            Vector3D wagonForward = Me.WorldMatrix.Forward;
            Vector3D wagonLeft = -Me.WorldMatrix.Right;
            double forwardAlignment = Vector3D.Dot(wagonForward, trainForward);
            double leftAlignment = Vector3D.Dot(wagonLeft, trainLeft);

            if (Math.Abs(forwardAlignment) < MIN_ALIGNMENT ||
                Math.Abs(leftAlignment) < MIN_ALIGNMENT ||
                forwardAlignment * leftAlignment <= 0)
            {
                CloseTaggedDoors();
                Echo("Orientation is ambiguous; tagged doors closed.");
                return;
            }

            bool sameDirection = forwardAlignment > 0;
            bool openLocalLeft = sameDirection ? openTrainLeft : openTrainRight;
            bool openLocalRight = sameDirection ? openTrainRight : openTrainLeft;

            SetDoors(LeftDoors, openLocalLeft);
            SetDoors(RightDoors, openLocalRight);

            if ((openLocalLeft && LeftDoors.Count == 0) ||
                (openLocalRight && RightDoors.Count == 0))
            {
                Echo("No tagged doors found on the requested side.");
            }
            else
            {
                Echo("Opened train " + (openTrainLeft ? "left" : "right") +
                     " side; wagon orientation " +
                     (sameDirection ? "forward" : "reversed") + ".");
            }
        }

        private void RefreshDoors()
        {
            LeftDoors.Clear();
            RightDoors.Clear();

            var doors = new List<IMyDoor>();
            GridTerminalSystem.GetBlocksOfType(doors, door => door.CubeGrid == Me.CubeGrid);

            foreach (IMyDoor door in doors)
            {
                bool left = HasTag(door.CustomName, LEFT_DOOR_TAG);
                bool right = HasTag(door.CustomName, RIGHT_DOOR_TAG);

                if (left && !right)
                {
                    LeftDoors.Add(door);
                }
                else if (right && !left)
                {
                    RightDoors.Add(door);
                }
                else if (left && right)
                {
                    Echo("Door has both side tags and will be ignored: " + door.CustomName);
                }
            }
        }

        private void CloseTaggedDoors()
        {
            RefreshDoors();
            SetDoors(LeftDoors, false);
            SetDoors(RightDoors, false);
        }

        private void SetDoors(List<IMyDoor> doors, bool open)
        {
            foreach (IMyDoor door in doors)
            {
                door.Enabled = true;
                if (open)
                {
                    door.OpenDoor();
                }
                else
                {
                    door.CloseDoor();
                }
            }

            DoorTransitionPending = true;
            Runtime.UpdateFrequency = UpdateFrequency.Update10;
        }

        private void UpdateDoorAvailability()
        {
            if (!DoorTransitionPending)
            {
                return;
            }

            bool leftSettled = DisableSettledDoors(LeftDoors);
            bool rightSettled = DisableSettledDoors(RightDoors);

            if (leftSettled && rightSettled)
            {
                DoorTransitionPending = false;
                Runtime.UpdateFrequency = UpdateFrequency.Update100;
                Echo("Doors reached their target state and were disabled.");
            }
        }

        private static bool DisableSettledDoors(List<IMyDoor> doors)
        {
            bool allSettled = true;
            foreach (IMyDoor door in doors)
            {
                if (door.Status == DoorStatus.Open || door.Status == DoorStatus.Closed)
                {
                    door.Enabled = false;
                }
                else
                {
                    door.Enabled = true;
                    allSettled = false;
                }
            }

            return allSettled;
        }

        private static bool HasTag(string name, string tag)
        {
            return name.IndexOf(tag, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool TryParseCommand(
            string payload,
            out Vector3D trainForward,
            out Vector3D trainLeft,
            out bool openTrainLeft,
            out bool openTrainRight,
            out string errorMessage)
        {
            trainForward = Vector3D.Zero;
            trainLeft = Vector3D.Zero;
            openTrainLeft = false;
            openTrainRight = false;
            errorMessage = null;

            string[] values = payload.Split(';');
            if (values.Length != 9)
            {
                errorMessage = "Invalid command format: expected 9 values, got " + values.Length + ".";
                return false;
            }

            if (values[0] != "1")
            {
                errorMessage = "Unsupported command version: " + values[0] + ".";
                return false;
            }

            if (!TryReadAxis(values, 1, "forward", out trainForward, out errorMessage) ||
                !TryReadAxis(values, 4, "left", out trainLeft, out errorMessage) ||
                !TryReadFlag(values[7], out openTrainLeft, out errorMessage) ||
                !TryReadFlag(values[8], out openTrainRight, out errorMessage))
            {
                return false;
            }

            return true;
        }

        private static bool TryReadAxis(
            string[] values,
            int start,
            string axisName,
            out Vector3D axis,
            out string errorMessage)
        {
            double x;
            double y;
            double z;
            axis = Vector3D.Zero;
            errorMessage = null;

            if (!double.TryParse(values[start], out x) ||
                !double.TryParse(values[start + 1], out y) ||
                !double.TryParse(values[start + 2], out z))
            {
                errorMessage = "Invalid " + axisName + " axis values: " +
                               values[start] + "," + values[start + 1] + "," + values[start + 2] + ".";
                return false;
            }

            axis = new Vector3D(x, y, z);
            double lengthSquared = axis.LengthSquared();
            if (double.IsNaN(lengthSquared) || double.IsInfinity(lengthSquared) ||
                Math.Abs(lengthSquared - 1.0) > 0.01)
            {
                errorMessage = "Invalid " + axisName + " axis length: " + lengthSquared + ".";
                return false;
            }

            axis = axis / Math.Sqrt(lengthSquared);
            return true;
        }

        private static bool TryReadFlag(string value, out bool flag, out string errorMessage)
        {
            errorMessage = null;

            flag = value == "1";
            bool ok = flag || value == "0";
            if(!ok)
            {
                errorMessage = "Failed to parse flag value: " + value;
            }
            return ok;
        }
    }
}
