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
        private const string LEFT_DOOR_TAG = "[DOOR_LEFT]";
        private const string RIGHT_DOOR_TAG = "[DOOR_RIGHT]";
        private const string COUPLER_TAG = "(train coupler)";
        private const string TRAIN_LIGHT_TAG = "(train light)";
        private const string SIDE_1_TAG = "(side 1)";
        private const string SIDE_2_TAG = "(side 2)";
        private const double MIN_ALIGNMENT = 0.5;
        private const double MAX_CONNECTOR_DISTANCE = 1.0;
        private const int MAX_REMEMBERED_COMMANDS = 64;
        private const float FRONT_LIGHT_RADIUS = 50f;
        private const float REAR_LIGHT_RADIUS = 1f;

        private readonly IMyBroadcastListener CommandListener;

        private readonly List<IMyDoor> LeftDoors = new List<IMyDoor>();
        private readonly List<IMyDoor> RightDoors = new List<IMyDoor>();
        private readonly List<IMyTerminalBlock> Couplers = new List<IMyTerminalBlock>();
        private readonly List<IMyLightingBlock> TrainLights = new List<IMyLightingBlock>();
        private readonly Queue<string> SeenCommandOrder = new Queue<string>();
        private readonly HashSet<string> SeenCommandIds = new HashSet<string>();
        private bool DoorTransitionPending;
        private bool Backward;
        private bool OpenLeft;
        private bool OpenRight;
        private long CommandSequence;
        private long ActiveLocomotiveId;
        private int ActiveDirection;
        private int FrontSide;
        private bool ActiveStateKnown;
        private bool ActiveStateChanged;

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update100;

            CommandListener = IGC.RegisterBroadcastListener(CHANNEL);
            CommandListener.SetMessageCallback(MESSAGE_CALLBACK);
            LoadState();
        }

        public void Save()
        {
            Storage = (Backward ? "1" : "0") + "|" +
                      (OpenLeft ? "1" : "0") + "|" +
                      (OpenRight ? "1" : "0") + "|" +
                      ActiveLocomotiveId.ToString() + "|" +
                      ActiveDirection.ToString() + "|" +
                      FrontSide.ToString() + "|" +
                      (ActiveStateKnown ? "1" : "0") + "|" +
                      CommandSequence.ToString();
        }

        public void Main(string argument, UpdateType updateSource)
        {
            ReadIncomingCommands();

            ActiveStateChanged = false;
            bool hasCommand = !string.IsNullOrWhiteSpace(argument) &&
                              argument != MESSAGE_CALLBACK;
            if (hasCommand && !ApplyCommand(argument))
            {
                Echo("Unknown command or missing side tags. Use forward/backward, " +
                     "open/close/switch, principal(e) sens 1/2, or inactive; " +
                     "couplers and lights need exactly one (side 1)/(side 2) tag.");
                UpdateDoorAvailability();
                return;
            }

            if (hasCommand)
            {
                Vector3D trainForward = Backward ? -Me.WorldMatrix.Forward : Me.WorldMatrix.Forward;
                Vector3D trainLeft = Backward ? Me.WorldMatrix.Right : -Me.WorldMatrix.Right;
                ApplyDoorCommand(trainForward, trainLeft, OpenLeft, OpenRight);
                BroadcastCommand(ActiveStateChanged);
                Save();
                ActiveStateChanged = false;
            }

            UpdateTrainLights();
            UpdateDoorAvailability();
            Echo("Direction: " + (Backward ? "backward" : "forward"));
            Echo("Train-left doors: " + (OpenLeft ? "open" : "closed"));
            Echo("Train-right doors: " + (OpenRight ? "open" : "closed"));
            Echo("Active locomotive: " +
                 (ActiveDirection == 0 ? "none" : ActiveDirection == 1 ? "sense 1" : "sense 2"));
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
                    CloseTaggedDoors();
                    Echo("Invalid door command payload; tagged doors closed.");
                    continue;
                }

                string commandId;
                Vector3D senderConnectorPosition;
                Vector3D trainForward;
                Vector3D trainLeft;
                bool openTrainLeft;
                bool openTrainRight;
                bool activeStateChanged;
                bool outgoingConnectorTowardFront;
                long activeLocomotiveId;
                int activeDirection;
                string errorMessage;
                if (!TryParseCommand(payload, out commandId, out senderConnectorPosition,
                    out trainForward, out trainLeft, out openTrainLeft,
                    out openTrainRight, out activeStateChanged,
                    out activeLocomotiveId, out activeDirection,
                    out outgoingConnectorTowardFront, out errorMessage))
                {
                    CloseTaggedDoors();
                    Echo("Invalid door command; tagged doors closed.");
                    Echo("Payload: " + payload);
                    Echo("Error: " + errorMessage);
                    continue;
                }

                IMyTerminalBlock incomingConnector;
                if (!TryFindIncomingConnector(senderConnectorPosition, out incomingConnector))
                {
                    Echo("Ignored door command: no coupled connector within " +
                         MAX_CONNECTOR_DISTANCE + " m.");
                    continue;
                }

                int incomingSide = GetSideNumber(incomingConnector.CustomName);
                if (incomingSide == 0)
                {
                    Echo("Ignored door command: incoming coupler needs exactly one side tag.");
                    continue;
                }

                if (!RememberCommand(commandId))
                {
                    continue;
                }

                OpenLeft = openTrainLeft;
                OpenRight = openTrainRight;

                if (activeStateChanged || !ActiveStateKnown)
                {
                    ActiveLocomotiveId = activeLocomotiveId;
                    ActiveDirection = activeDirection;
                    ActiveStateKnown = true;
                    FrontSide = activeDirection == 0
                        ? 0
                        : GetFrontSide(incomingSide, outgoingConnectorTowardFront);
                    if (activeDirection != 0)
                    {
                        SetBackwardFromFrontSide();
                    }
                }
                else if (activeDirection != 0 &&
                         activeLocomotiveId == ActiveLocomotiveId &&
                         activeDirection == ActiveDirection)
                {
                    FrontSide = GetFrontSide(incomingSide, outgoingConnectorTowardFront);
                    SetBackwardFromFrontSide();
                }
                ApplyDoorCommand(trainForward, trainLeft, openTrainLeft, openTrainRight);
                Save();
                BroadcastCommand(commandId, trainForward, trainLeft,
                    openTrainLeft, openTrainRight, incomingConnector,
                    activeStateChanged);
            }
        }

        private void LoadState()
        {
            Backward = false;
            OpenLeft = false;
            OpenRight = false;
            ActiveLocomotiveId = 0;
            ActiveDirection = 0;
            FrontSide = 0;
            ActiveStateKnown = false;

            string storedState = Storage ?? string.Empty;
            if (storedState.IndexOf('|') < 0)
            {
                string[] legacyValues = storedState.Split(',');
                if (legacyValues.Length == 3 &&
                    TryParseFlag(legacyValues[0], out Backward) &&
                    TryParseFlag(legacyValues[1], out OpenLeft) &&
                    TryParseFlag(legacyValues[2], out OpenRight))
                {
                    return;
                }

                Backward = false;
                OpenLeft = false;
                OpenRight = false;
                return;
            }

            string[] values = storedState.Split('|');
            long activeLocomotiveId;
            int activeDirection;
            int frontSide;
            bool activeStateKnown;
            long commandSequence = 0;
            if (values.Length != 8 ||
                !TryParseFlag(values[0], out Backward) ||
                !TryParseFlag(values[1], out OpenLeft) ||
                !TryParseFlag(values[2], out OpenRight) ||
                !long.TryParse(values[3], out activeLocomotiveId) ||
                !int.TryParse(values[4], out activeDirection) ||
                !int.TryParse(values[5], out frontSide) ||
                !TryParseFlag(values[6], out activeStateKnown) ||
                activeDirection < 0 || activeDirection > 2 ||
                frontSide < 0 || frontSide > 2 ||
                (activeDirection == 0 && activeLocomotiveId != 0) ||
                (activeDirection == 0 && frontSide != 0) ||
                (activeDirection != 0 && activeLocomotiveId <= 0) ||
                (activeDirection != 0 && frontSide == 0) ||
                (activeDirection != 0 && !activeStateKnown) ||
                !long.TryParse(values[7], out commandSequence) ||
                commandSequence < 0)
            {
                Backward = false;
                OpenLeft = false;
                OpenRight = false;
                return;
            }

            ActiveLocomotiveId = activeLocomotiveId;
            ActiveDirection = activeDirection;
            FrontSide = activeDirection == 0 ? 0 : frontSide;
            ActiveStateKnown = activeStateKnown;
            CommandSequence = commandSequence;
        }

        private bool ApplyCommand(string argument)
        {
            string[] parts = argument.Trim().ToLowerInvariant()
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 1 && parts[0] == "forward")
            {
                if (ActiveLocomotiveId == Me.EntityId && ActiveDirection != 0)
                {
                    int localForwardSide;
                    if (!TryGetForwardSide(out localForwardSide))
                    {
                        return false;
                    }

                    ActiveDirection = 1;
                    FrontSide = localForwardSide;
                    ActiveStateKnown = true;
                    ActiveStateChanged = true;
                }

                Backward = false;
                return true;
            }

            if (parts.Length == 1 && parts[0] == "backward")
            {
                if (ActiveLocomotiveId == Me.EntityId && ActiveDirection != 0)
                {
                    int localForwardSide;
                    if (!TryGetForwardSide(out localForwardSide))
                    {
                        return false;
                    }

                    ActiveDirection = 2;
                    FrontSide = GetOppositeSide(localForwardSide);
                    ActiveStateKnown = true;
                    ActiveStateChanged = true;
                }

                Backward = true;
                return true;
            }

            if (parts.Length == 3 &&
                (parts[0] == "principale" || parts[0] == "principal") &&
                parts[1] == "sens" &&
                (parts[2] == "1" || parts[2] == "2"))
            {
                int localForwardSide;
                if (!TryGetForwardSide(out localForwardSide))
                {
                    return false;
                }

                ActiveLocomotiveId = Me.EntityId;
                ActiveDirection = parts[2] == "1" ? 1 : 2;
                Backward = ActiveDirection == 2;
                FrontSide = ActiveDirection == 1
                    ? localForwardSide
                    : GetOppositeSide(localForwardSide);
                ActiveStateKnown = true;
                ActiveStateChanged = true;
                return true;
            }

            if (parts.Length == 1 && parts[0] == "inactive")
            {
                if (ActiveLocomotiveId == Me.EntityId)
                {
                    ActiveLocomotiveId = 0;
                    ActiveDirection = 0;
                    FrontSide = 0;
                    ActiveStateKnown = true;
                    ActiveStateChanged = true;
                }

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

        private bool TryFindIncomingConnector(
            Vector3D senderConnectorPosition,
            out IMyTerminalBlock incomingConnector)
        {
            RefreshCouplers();

            incomingConnector = null;
            double nearestDistanceSquared = MAX_CONNECTOR_DISTANCE * MAX_CONNECTOR_DISTANCE;
            foreach (IMyTerminalBlock connector in Couplers)
            {
                if (!connector.GetValueBool("CouplingDevice_Coupled"))
                {
                    continue;
                }

                double distanceSquared =
                    Vector3D.DistanceSquared(connector.GetPosition(), senderConnectorPosition);
                if (distanceSquared <= nearestDistanceSquared)
                {
                    nearestDistanceSquared = distanceSquared;
                    incomingConnector = connector;
                }
            }

            return incomingConnector != null;
        }

        private void RefreshCouplers()
        {
            GridTerminalSystem.GetBlocksOfType(
                Couplers,
                block => block.CubeGrid == Me.CubeGrid &&
                         HasTag(block.CustomName, COUPLER_TAG));
        }

        private bool TryGetForwardSide(out int forwardSide)
        {
            RefreshCouplers();
            RefreshTrainLights();

            double side1Projection = 0;
            double side2Projection = 0;
            int side1Count = 0;
            int side2Count = 0;
            Vector3D localForward = Me.WorldMatrix.Forward;

            foreach (IMyTerminalBlock coupler in Couplers)
            {
                AddSideProjection(
                    coupler.CustomName,
                    coupler.GetPosition(),
                    localForward,
                    ref side1Projection,
                    ref side1Count,
                    ref side2Projection,
                    ref side2Count);
            }

            foreach (IMyLightingBlock light in TrainLights)
            {
                AddSideProjection(
                    light.CustomName,
                    light.GetPosition(),
                    localForward,
                    ref side1Projection,
                    ref side1Count,
                    ref side2Projection,
                    ref side2Count);
            }

            if (side1Count == 0 || side2Count == 0)
            {
                forwardSide = 0;
                return false;
            }

            double sideDifference =
                side1Projection / side1Count - side2Projection / side2Count;
            if (Math.Abs(sideDifference) < 0.1)
            {
                forwardSide = 0;
                return false;
            }

            forwardSide = sideDifference > 0 ? 1 : 2;
            return true;
        }

        private void AddSideProjection(
            string blockName,
            Vector3D position,
            Vector3D localForward,
            ref double side1Projection,
            ref int side1Count,
            ref double side2Projection,
            ref int side2Count)
        {
            int side = GetSideNumber(blockName);
            double projection = Vector3D.Dot(position - Me.GetPosition(), localForward);
            if (side == 1)
            {
                side1Projection += projection;
                side1Count++;
            }
            else if (side == 2)
            {
                side2Projection += projection;
                side2Count++;
            }
        }

        private void SetBackwardFromFrontSide()
        {
            int localForwardSide;
            if (TryGetForwardSide(out localForwardSide))
            {
                Backward = FrontSide != localForwardSide;
            }
        }

        private static int GetFrontSide(int incomingSide, bool outgoingConnectorTowardFront)
        {
            return outgoingConnectorTowardFront
                ? GetOppositeSide(incomingSide)
                : incomingSide;
        }

        private static int GetOppositeSide(int side)
        {
            return side == 1 ? 2 : 1;
        }

        private static int GetSideNumber(string blockName)
        {
            bool side1 = HasTag(blockName, SIDE_1_TAG);
            bool side2 = HasTag(blockName, SIDE_2_TAG);
            if (side1 == side2)
            {
                return 0;
            }

            return side1 ? 1 : 2;
        }

        private bool RememberCommand(string commandId)
        {
            if (!SeenCommandIds.Add(commandId))
            {
                return false;
            }

            SeenCommandOrder.Enqueue(commandId);
            while (SeenCommandOrder.Count > MAX_REMEMBERED_COMMANDS)
            {
                SeenCommandIds.Remove(SeenCommandOrder.Dequeue());
            }

            return true;
        }

        private void BroadcastCommand(bool activeStateChanged)
        {
            Vector3D trainForward = Backward ? -Me.WorldMatrix.Forward : Me.WorldMatrix.Forward;
            Vector3D trainLeft = Backward ? Me.WorldMatrix.Right : -Me.WorldMatrix.Right;
            string commandId = Me.EntityId.ToString() + ":" +
                               (++CommandSequence).ToString();
            RememberCommand(commandId);
            BroadcastCommand(commandId, trainForward, trainLeft, OpenLeft, OpenRight,
                null, activeStateChanged);
        }

        private void BroadcastCommand(
            string commandId,
            Vector3D trainForward,
            Vector3D trainLeft,
            bool openTrainLeft,
            bool openTrainRight,
            IMyTerminalBlock incomingConnector,
            bool activeStateChanged)
        {
            RefreshCouplers();

            foreach (IMyTerminalBlock connector in Couplers)
            {
                if ((incomingConnector != null &&
                     connector.EntityId == incomingConnector.EntityId) ||
                    !connector.GetValueBool("CouplingDevice_Coupled"))
                {
                    continue;
                }

                int connectorSide = GetSideNumber(connector.CustomName);
                if (connectorSide == 0)
                {
                    Echo("Coupled train connector needs exactly one side tag.");
                    continue;
                }

                bool outgoingConnectorTowardFront = ActiveDirection != 0 &&
                    connectorSide == FrontSide;

                string payload = "3;" + commandId + ";" +
                                 FormatVector(connector.GetPosition()) + ";" +
                                 FormatVector(trainForward) + ";" +
                                 FormatVector(trainLeft) + ";" +
                                 (openTrainLeft ? "1" : "0") + ";" +
                                 (openTrainRight ? "1" : "0") + ";" +
                                 (activeStateChanged ? "1" : "0") + ";" +
                                 ActiveLocomotiveId.ToString() + ";" +
                                 ActiveDirection.ToString() + ";" +
                                 (outgoingConnectorTowardFront ? "1" : "0");

                IGC.SendBroadcastMessage(
                    CHANNEL,
                    payload,
                    TransmissionDistance.ConnectedConstructs);
            }
        }

        private static string FormatVector(Vector3D value)
        {
            return value.X.ToString() + ";" +
                   value.Y.ToString() + ";" +
                   value.Z.ToString();
        }

        private void UpdateTrainLights()
        {
            RefreshTrainLights();
            if (TrainLights.Count == 0)
            {
                return;
            }

            RefreshCouplers();
            bool side1Connected = false;
            bool side2Connected = false;
            bool invalidCoupledSide = false;

            foreach (IMyTerminalBlock coupler in Couplers)
            {
                if (!coupler.GetValueBool("CouplingDevice_Coupled"))
                {
                    continue;
                }

                int side = GetSideNumber(coupler.CustomName);
                if (side == 1)
                {
                    side1Connected = true;
                }
                else if (side == 2)
                {
                    side2Connected = true;
                }
                else
                {
                    invalidCoupledSide = true;
                }
            }

            foreach (IMyLightingBlock light in TrainLights)
            {
                int side = GetSideNumber(light.CustomName);
                if (side == 0 || invalidCoupledSide)
                {
                    light.Enabled = false;
                    continue;
                }

                bool sideConnected = side == 1
                    ? side1Connected
                    : side2Connected;

                if (sideConnected)
                {
                    light.Enabled = false;
                    continue;
                }

                bool isTrainFront = ActiveDirection != 0 &&
                                    side == FrontSide;

                light.Enabled = true;
                light.Color = isTrainFront ? Color.White : Color.Red;
                light.Radius = isTrainFront ? FRONT_LIGHT_RADIUS : REAR_LIGHT_RADIUS;
            }
        }

        private void RefreshTrainLights()
        {
            GridTerminalSystem.GetBlocksOfType(
                TrainLights,
                light => light.CubeGrid == Me.CubeGrid &&
                         HasTag(light.CustomName, TRAIN_LIGHT_TAG));
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
            out string commandId,
            out Vector3D senderConnectorPosition,
            out Vector3D trainForward,
            out Vector3D trainLeft,
            out bool openTrainLeft,
            out bool openTrainRight,
            out bool activeStateChanged,
            out long activeLocomotiveId,
            out int activeDirection,
            out bool outgoingConnectorTowardFront,
            out string errorMessage)
        {
            commandId = null;
            senderConnectorPosition = Vector3D.Zero;
            trainForward = Vector3D.Zero;
            trainLeft = Vector3D.Zero;
            openTrainLeft = false;
            openTrainRight = false;
            activeStateChanged = false;
            activeLocomotiveId = 0;
            activeDirection = 0;
            outgoingConnectorTowardFront = false;
            errorMessage = null;

            string[] values = payload.Split(';');
            if (values.Length != 17)
            {
                errorMessage = "Invalid command format: expected 17 values, got " + values.Length + ".";
                return false;
            }

            if (values[0] != "3")
            {
                errorMessage = "Unsupported command version: " + values[0] + ".";
                return false;
            }

            if (string.IsNullOrWhiteSpace(values[1]))
            {
                errorMessage = "Command ID is empty.";
                return false;
            }

            if (!TryReadVector(values, 2, "sender connector position",
                    out senderConnectorPosition, out errorMessage) ||
                !TryReadAxis(values, 5, "forward", out trainForward, out errorMessage) ||
                !TryReadAxis(values, 8, "left", out trainLeft, out errorMessage) ||
                !TryReadFlag(values[11], out openTrainLeft, out errorMessage) ||
                !TryReadFlag(values[12], out openTrainRight, out errorMessage) ||
                !TryReadFlag(values[13], out activeStateChanged, out errorMessage) ||
                !long.TryParse(values[14], out activeLocomotiveId) ||
                !int.TryParse(values[15], out activeDirection) ||
                !TryReadFlag(values[16], out outgoingConnectorTowardFront, out errorMessage))
            {
                if (errorMessage == null)
                {
                    errorMessage = "Invalid active locomotive state.";
                }
                return false;
            }

            if (activeDirection < 0 || activeDirection > 2 ||
                (activeDirection == 0 && activeLocomotiveId != 0) ||
                (activeDirection != 0 && activeLocomotiveId <= 0) ||
                (activeDirection == 0 && outgoingConnectorTowardFront))
            {
                errorMessage = "Invalid active locomotive state.";
                return false;
            }

            commandId = values[1];
            return true;
        }

        private static bool TryReadVector(
            string[] values,
            int start,
            string vectorName,
            out Vector3D vector,
            out string errorMessage)
        {
            double x;
            double y;
            double z;
            vector = Vector3D.Zero;
            errorMessage = null;

            if (!double.TryParse(values[start], out x) ||
                !double.TryParse(values[start + 1], out y) ||
                !double.TryParse(values[start + 2], out z) ||
                double.IsNaN(x) || double.IsInfinity(x) ||
                double.IsNaN(y) || double.IsInfinity(y) ||
                double.IsNaN(z) || double.IsInfinity(z))
            {
                errorMessage = "Invalid " + vectorName + " values.";
                return false;
            }

            vector = new Vector3D(x, y, z);
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

        private static bool TryParseFlag(string value, out bool flag)
        {
            flag = value == "1";
            return flag || value == "0";
        }
    }
}
