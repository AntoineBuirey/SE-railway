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
    public static class TAGS
    {
        
        public const string ICS_SIGNAL2SIGNAL_TAG = "SER_SIGNAL_TO_SIGNAL";
        public const string ICS_SIGNAL2TRAIN_TAG = "SER_SIGNAL_TO_TRAIN";
        public const string ICS_SERVER2SIGNAL_TAG = "SER_SERVER_TO_SIGNAL";
        public const string ICS_SIGNAL2SERVER_TAG = "SER_SIGNAL_TO_SERVER";
        public const string ICS_SIGNAL_BROADCAST_BLOCKS_TAG = "SER_SIGNAL_BROADCAST_BLOCKS";

    }
    public static class Constants
    {
        public const int ALLOWED_MISSED_MESSAGES = 10;
        public const int MAX_TRAIN_SPEED = 100;
        public const int MAX_TRAIN_SPEED_WARNING = 100;
        public const int MIN_TRAIN_SPEED_WARNING = 10;
        public const int MAX_TRAIN_SPEED_ERROR = 20;
    }

    public enum Status
    {
        CLOSED,     // full red
        OPENED,     // full green
        WARNING,    // full yellow
        ERROR,      // flashing red
    }

    public static class Colors
    {
        public static readonly Color RED = new Color(255, 0, 0);
        public static readonly Color GREEN = new Color(0, 255, 0);
        public static readonly Color YELLOW = new Color(255, 255, 0);
        public static readonly Color WHITE = new Color(255, 255, 255);
    }

    [Flags]
    public enum ErrorCode : short
    {
        NONE = 0,
        SIGNAL_NOT_RESPONDING = 1,
        MALFORMED_CUSTOM_DATA = 2,
        MALFORMED_MESSAGE = 4,
        INVALID_SPEED = 8,
        INVALID_STATUS = 16,
        TOO_MUCH_ENTITIES_DETECTED = 32,
        TRAIN_TOO_FAST = 64,
        UNKNOWN_TAG = 128,
    }

    public enum TrainStatus : short
    {
        FORWARD,
        BACKWARD,
        STOPPED,
        ERROR
    }

    public static class SpecialBlocksId
    {
        public const int TERMINUS = -1;
        public const int UNDEFINED = -2;
    }

    public enum BlockSignalRelation
    {
        NONE,
        PREVIOUS,
        NEXT
    }

    public enum SignalSignalRelation
    {
        NONE,
        PREVIOUS,
        NEXT,
        PREVIOUS_PARALLEL,
        NEXT_PARALLEL
    }

    public static class CDataKeys
    {
        // editable fields by the user
        public const string NextBlockId = "NextBlockId";
        public const string PreviousBlockId = "PreviousBlockId";
        

        // internal fields, should not be edited by the user (they start by a @)

    }
}