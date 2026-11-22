#pragma warning disable IDE0290

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
    public class Signal
    {
        public int Address { get; }
        public Block PreviousBlock { get; private set; }
        public Block NextBlock { get; private set; }

        private Status SignalStatus;
        
        private readonly IMyIntergridCommunicationSystem IGC;
        
        public Signal(IMyIntergridCommunicationSystem igc, int address, Block previousBlock, Block nextBlock)
        {
            Address = address;
            PreviousBlock = previousBlock;
            NextBlock = nextBlock;
            IGC = igc;
        }

        /**
        * tag : ICS_SIGNAL_BROADCAST_BLOCKS_TAG
        * create a Signal object from a string in the format "address,previousBlockId,nextBlockId"
        */
        public static Signal HandleBroadcast(IMyIntergridCommunicationSystem igc, string message, Dictionary<string, Block> blocks, int defaultBlockSpeed)
        {
            string[] parts = message.Split(',');
            if (parts.Length != 3)
                throw new ArgumentException($"Invalid signal message: {message}");

            int address = int.Parse(parts[0]);
            string previousBlockId = parts[1];
            string nextBlockId = parts[2];

            // create blocks if they don't exist
            if (!blocks.ContainsKey(previousBlockId))
                blocks[previousBlockId] = new Block(previousBlockId, defaultBlockSpeed);
            if (!blocks.ContainsKey(nextBlockId))
                blocks[nextBlockId] = new Block(nextBlockId, defaultBlockSpeed);

            Signal newSignal = new Signal(igc, address, blocks[previousBlockId], blocks[nextBlockId]);

            // add the signal to the blocks
            blocks[previousBlockId].AddExit(newSignal);
            blocks[nextBlockId].AddEntrance(newSignal);
        }

        /**
        * tag : SER_SIGNAL_TO_SERVER
        * handle updates from the signal, in the format "TrainStatus"
        */
        public void HandleUpdate(string message)
        {
            string[] parts = message.Split(',');
            if (parts.Length != 1)
                throw new ArgumentException($"Invalid signal update message: {message}");


            TrainStatus trainStatus;
            if (!Enum.TryParse(parts[0], out trainStatus))
                throw new ArgumentException($"Invalid train status: {parts[0]}");

            switch (trainStatus)
            {
                case TrainStatus.FORWARD:
                    PreviousBlock.DecrementTrains();
                    NextBlock.IncrementTrains();
                    break;
                case TrainStatus.BACKWARD:
                    PreviousBlock.IncrementTrains();
                    NextBlock.DecrementTrains();
                    break;
                case TrainStatus.STOPPED:
                    // do nothing
                    break;
                case TrainStatus.ERROR:
                    // do nothing
                default:
                    throw new InvalidOperationException($"Invalid train status: {trainStatus}");
            }
        }


        /**
        * calculate the speed the signal should display and pass to the train, based on the original speed, and the status of the signal
        */
        private int CalculateSpeed()
        {
            switch (SignalStatus)
            {
                case Status.CLOSED:
                    return 0;
                case Status.OPENED:
                    return Math.Min(Constants.MAX_TRAIN_SPEED, NextBlock.Speed);
                case Status.WARNING:
                    return Math.Min(
                        Constants.MAX_TRAIN_SPEED_WARNING,
                        Math.Max(
                            Constants.MIN_TRAIN_SPEED_WARNING,
                            NextBlock.Speed/2
                            )
                        );
                case Status.ERROR:
                    return Math.Min(Constants.MAX_TRAIN_SPEED_ERROR, NextBlock.Speed);
                default:
                    throw new InvalidOperationException();
            }
        }

        public void SetStatus(Status status)
        {
            SignalStatus = status;
            int speed = CalculateSpeed();
            IGC.SendUnicastMessage(Address, TAGS.ICS_SERVER2SIGNAL_TAG, $"{status},{speed}");
        }

        public Status GetStatus()
        {
            return SignalStatus;
        }
    }
}