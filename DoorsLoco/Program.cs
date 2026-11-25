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
        private const string MESSAGE_CALLBACK = "doors-command";
        private const double MIN_ALIGNMENT = 0.5;

        private readonly IMyBroadcastListener CommandListener;

        private bool Backward;
        private bool OpenLeft;
        private bool OpenRight;
        private bool InitialBroadcastPending = true;

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update100;
            CommandListener = IGC.RegisterBroadcastListener(CHANNEL);
            CommandListener.SetMessageCallback(MESSAGE_CALLBACK);

            LoadState();
        }

        public void Save()
        {
            Storage = (Backward ? "1" : "0") + "," +
                      (OpenLeft ? "1" : "0") + "," +
                      (OpenRight ? "1" : "0");
        }

        public void Main(string argument, UpdateType updateSource)
        {
            ReadIncomingCommands();

            bool hasCommand = !string.IsNullOrWhiteSpace(argument) &&
                              argument != MESSAGE_CALLBACK;
            if (hasCommand && !ApplyCommand(argument))
            {
                Echo("Unknown command. Use forward, backward, open left/right/both, or close.");
                return;
            }

            if (hasCommand || InitialBroadcastPending)
            {
                BroadcastCommand();
                InitialBroadcastPending = false;
            }

            Echo("Direction: " + (Backward ? "backward" : "forward"));
            Echo("Train-left doors: " + (OpenLeft ? "open" : "closed"));
            Echo("Train-right doors: " + (OpenRight ? "open" : "closed"));
        }

        private void ReadIncomingCommands()
        {
            while (CommandListener.HasPendingMessage)
            {
                var message = CommandListener.AcceptMessage();
                if (message.Tag != CHANNEL)
                {
                    continue;
                }

                string payload = message.Data as string;
                if (payload == null)
                {
                    Echo("Ignored locomotive message: payload is not a string.");
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
                    Echo("Ignored invalid locomotive message: " + errorMessage);
                    continue;
                }

                Vector3D localForward = Backward ? -Me.WorldMatrix.Forward : Me.WorldMatrix.Forward;
                Vector3D localLeft = Backward ? Me.WorldMatrix.Right : -Me.WorldMatrix.Right;
                double forwardAlignment = Vector3D.Dot(localForward, trainForward);
                double leftAlignment = Vector3D.Dot(localLeft, trainLeft);
                if (Math.Abs(forwardAlignment) < MIN_ALIGNMENT ||
                    Math.Abs(leftAlignment) < MIN_ALIGNMENT ||
                    forwardAlignment * leftAlignment <= 0)
                {
                    Echo("Ignored locomotive message: orientation is ambiguous.");
                    continue;
                }

                bool sameDirection = forwardAlignment > 0;
                OpenLeft = sameDirection ? openTrainLeft : openTrainRight;
                OpenRight = sameDirection ? openTrainRight : openTrainLeft;
            }
        }

        private void LoadState()
        {
            Backward = false;
            OpenLeft = false;
            OpenRight = false;

            string[] values = (Storage ?? string.Empty).Split(',');
            if (values.Length != 3 ||
                !TryParseFlag(values[0], out Backward) ||
                !TryParseFlag(values[1], out OpenLeft) ||
                !TryParseFlag(values[2], out OpenRight))
            {
                Backward = false;
                OpenLeft = false;
                OpenRight = false;
            }
        }

        private bool ApplyCommand(string argument)
        {
            string[] parts = argument.Trim().ToLowerInvariant()
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 1 && parts[0] == "forward")
            {
                Backward = false;
                return true;
            }

            if (parts.Length == 1 && parts[0] == "backward")
            {
                Backward = true;
                return true;
            }

            if (parts.Length == 1 && parts[0] == "close")
            {
                OpenLeft = false;
                OpenRight = false;
                return true;
            }

            if (parts.Length == 2 && parts[0] == "open")
            {
                if (parts[1] == "left")
                {
                    OpenLeft = true;
                    OpenRight = false;
                    return true;
                }

                if (parts[1] == "right")
                {
                    OpenLeft = false;
                    OpenRight = true;
                    return true;
                }

                if (parts[1] == "both")
                {
                    OpenLeft = true;
                    OpenRight = true;
                    return true;
                }
            }

            if (parts.Length == 2 && parts[0] == "close")
            {
                if (parts[1] == "left")
                {
                    OpenLeft = false;
                    return true;
                }

                if (parts[1] == "right")
                {
                    OpenRight = false;
                    return true;
                }

                if (parts[1] == "both")
                {
                    OpenLeft = false;
                    OpenRight = false;
                    return true;
                }
            }

            if (parts.Length == 2 && parts[0] == "switch")
            {
                if (parts[1] == "left")
                {
                    OpenLeft = !OpenLeft;
                    return true;
                }

                if (parts[1] == "right")
                {
                    OpenRight = !OpenRight;
                    return true;
                }

                if (parts[1] == "both")
                {
                    OpenLeft = !OpenLeft;
                    OpenRight = !OpenRight;
                    return true;
                }
            }

            return false;
        }

        private void BroadcastCommand()
        {
            Vector3D trainForward = Backward ? -Me.WorldMatrix.Forward : Me.WorldMatrix.Forward;
            Vector3D trainLeft = Backward ? Me.WorldMatrix.Right : -Me.WorldMatrix.Right;
            string message = "1;" + FormatAxis(trainForward) + ";" +
                             FormatAxis(trainLeft) + ";" +
                             (OpenLeft ? "1" : "0") + ";" +
                             (OpenRight ? "1" : "0");

            IGC.SendBroadcastMessage(
                CHANNEL,
                message,
                TransmissionDistance.ConnectedConstructs);
        }

        private static string FormatAxis(Vector3D axis)
        {
            return axis.X.ToString() + ";" +
                   axis.Y.ToString() + ";" +
                   axis.Z.ToString();
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
                errorMessage = "Expected 9 values, got " + values.Length + ".";
                return false;
            }

            if (values[0] != "1")
            {
                errorMessage = "Unsupported command version: " + values[0] + ".";
                return false;
            }

            if (!TryReadAxis(values, 1, "forward", out trainForward, out errorMessage) ||
                !TryReadAxis(values, 4, "left", out trainLeft, out errorMessage) ||
                !TryParseFlag(values[7], out openTrainLeft) ||
                !TryParseFlag(values[8], out openTrainRight))
            {
                if (errorMessage == null)
                {
                    errorMessage = "Invalid door-state flags.";
                }
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
                errorMessage = "Invalid " + axisName + " axis values.";
                return false;
            }

            axis = new Vector3D(x, y, z);
            double lengthSquared = axis.LengthSquared();
            if (double.IsNaN(lengthSquared) || double.IsInfinity(lengthSquared) ||
                Math.Abs(lengthSquared - 1.0) > 0.01)
            {
                errorMessage = "Invalid " + axisName + " axis length.";
                return false;
            }

            axis = axis / Math.Sqrt(lengthSquared);
            return true;
        }

        private static bool TryParseFlag(string value, out bool flag)
        {
            flag = value == "1";
            return flag || value == "0";
        }
    }
}
