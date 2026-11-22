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

using IngameScript;

namespace IngameScript
{
    public class OtherSignal
    {
        public long Address { get; set; }

        // status of the other signals, should never be edited directly,
        // except by the MessageHandler method.
        public int Speed { get; set; }
        public Status Status { get; set; }
        
        private int LastContact = 0;
        private readonly IMyIntergridCommunicationSystem IGC;
        private readonly CustomDataStorage Storage;

        public OtherSignal(IMyIntergridCommunicationSystem igc, CustomDataStorage storage, long address)
        {
            Address = address;
            IGC = igc;
            Storage = storage;
        }

        /**
        * Handle a message received from the other signal.
        */
        public ErrorCode MessageHandler(string tag, string message, long sourceAddress)
        {
            if (!sourceAddress.Equals(Address))
            {
                // this case should never happen, but we check it anyway to be safe.
                return ErrorCode.NONE;
            }

            LastContact = 0;

            if (tag.Equals(Constants.ICS_SIGNAL2SIGNAL_TAG))
            {
                return ParseInfra2InfraMessage(message);
            }
            else if (tag.Equals(Constants.ICS_INFRA_BROADCAST_BLOCKS_TAG))
            {
                return ParseBroadcastBlocksMessage(message);
            }

            return ErrorCode.UNKNOWN_TAG;
        }

        public void BroadcastNeighborBlocks(Block block, BlockSignalRelation relation)
        {
            // message format is: "blockId,nbTrainsInBlock,speed,lastUpdate,relation"
            string message = $"{block.MessageFormat()},{relation}";
            IGC.SendBroadcastMessage(Constants.ICS_INFRA_BROADCAST_BLOCKS_TAG, message, TransmissionDistance.TransmissionDistanceMax);
        }

        /**
        * Parse a message and update the status of the other signal.
        * a message format is: "speed,status,nbTrainsInBlock"
        */
        private ErrorCode ParseInfra2InfraMessage(string message)
        { 
            string[] parts = message.Split(',');
            if (parts.Length != 3)
            {
                return ErrorCode.MALFORMED_MESSAGE;
            }

            int newSpeed;
            if (!int.TryParse(parts[0], out newSpeed))
            {
                return ErrorCode.INVALID_SPEED;
            }

            Status newStatus;
            if (!Enum.TryParse(parts[1], out newStatus))
            {
                return ErrorCode.INVALID_STATUS;
            }

            int newNbTrainsInBlock;
            if (!int.TryParse(parts[2], out newNbTrainsInBlock))
            {
                return ErrorCode.INVALID_SPEED;
            }

            Speed = newSpeed;
            Status = newStatus;
            NbTrainsInBlock = newNbTrainsInBlock;

            return ErrorCode.NONE;
        }


        /**
        * Parse a broadcast message containing the neighbor blocks of another signal.
        * a message format is: "blockId,nbTrainsInBlock,speed,lastUpdate,relation"
        */
        private static ErrorCode ParseBroadcastBlocksMessage(Block previousBLock, Block nextBlock, string message, long sourceAddress)
        {
            string[] parts = message.Split(',');
            if (parts.Length != 5)
            {
                return ErrorCode.MALFORMED_MESSAGE;
            }

            long blockId;
            if (!long.TryParse(parts[0], out blockId))
            {
                return ErrorCode.MALFORMED_MESSAGE;
            }

            if (!(blockId == previousBLock.Id || blockId == nextBlock.Id))
            {
                // this block is not a neighbor of this signal, we ignore it.
                return ErrorCode.NONE;
            }

            int nbTrainsInBlock;
            if (!int.TryParse(parts[1], out nbTrainsInBlock))
            {
                return ErrorCode.MALFORMED_MESSAGE;
            }

            int speed;
            if (!int.TryParse(parts[2], out speed))
            {
                return ErrorCode.MALFORMED_MESSAGE;
            }

            long lastUpdateTicks;
            if (!long.TryParse(parts[3], out lastUpdateTicks))
            {
                return ErrorCode.MALFORMED_MESSAGE;
            }

            DateTime lastUpdate = DateTime.FromFileTimeUtc(lastUpdateTicks);

            BlockSignalRelation relation;
            if (!Enum.TryParse(parts[4], out relation))
            {
                return ErrorCode.MALFORMED_MESSAGE;
            }

            SignalSignalRelation signalRelation = SignalSignalRelation.NONE;

            if(blockId == previousBLock.Id)
            {
                if (relation == BlockSignalRelation.PREVIOUS)
                {
                    signalRelation = SignalSignalRelation.PREVIOUS_PARALLEL;
                }
                else if (relation == BlockSignalRelation.NEXT)
                {
                    signalRelation = SignalSignalRelation.PREVIOUS;
                }
                else
                {
                    return ErrorCode.MALFORMED_MESSAGE;
                }
            }
            else if (blockId == nextBlock.Id)
            {
                if (relation == BlockSignalRelation.PREVIOUS)
                {
                    signalRelation = SignalSignalRelation.NEXT;
                }
                else if (relation == BlockSignalRelation.NEXT)
                {
                    signalRelation = SignalSignalRelation.NEXT_PARALLEL;
                }
                else
                {
                    return ErrorCode.MALFORMED_MESSAGE;
                }
            }

            

        }


        /**
        * Send a message to the other signal.
        */
        private ErrorCode SendMessage(string tag, string message)
        {
            bool success = IGC.SendUnicastMessage(Address, tag, message);
            if (!success)
            {
                return ErrorCode.SIGNAL_NOT_RESPONDING;
            }

            return ErrorCode.NONE;
        }

        /**
        * Send the current status to the other signal.
        */
        public ErrorCode SendStatus(int speed, Status status, int nbTrainsInBlock)
        {
            string message = $"{speed},{status},{nbTrainsInBlock}";
            return SendMessage(Constants.ICS_SIGNAL2SIGNAL_TAG, message);
        }

        public ErrorCode SendUpdateBlockCounter(int nbToAdd)
        {
            string message = $"{nbToAdd}";
            return SendMessage(Constants.ICS_INFRA_UPDATE_BLOCK_COUNTER_TAG, message);
        }

        /**
        * Update the last contact counter and return an error code if the other signal is not responding.
        */
        public ErrorCode Update()
        {
            LastContact++;
            if (LastContact > Constants.ALLOWED_MISSED_MESSAGES)
            {
                return ErrorCode.SIGNAL_NOT_RESPONDING;
            }

            return ErrorCode.NONE;
        }
    }
}