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
    public class Block
    {
        public string Id { get; }
        public int Speed { get; private set; }
        private int NbTrains;
        private readonly List<Signal> Entrances = new List<Signal>();
        private readonly List<Signal> Exits = new List<Signal>();
        private uint ActiveExitId;

        public Block(string id, int speed)
        {
            Id = id;
            Speed = speed;
            NbTrains = 0;
            ActiveExitId = 0;
        }

        /**
        * create a Block object from a string in the format "nbTrains,speed"
        */
        public static Block FromString(string id, string str)
        {
            string[] parts = str.Split(',');
            if (parts.Length != 3)
                throw new ArgumentException($"Invalid block string: {str}");

            int nbTrains = int.Parse(parts[0]);
            int speed = int.Parse(parts[1]);

            return new Block(id, speed) { NbTrains = nbTrains };
        }

        /**
        * convert a Block object to a string in the format "nbTrains,speed"
        */
        public override string ToString()
        {
            return $"{NbTrains},{Speed}";
        }

        public void IncrementTrains()
        {
            NbTrains++;
        }

        public void DecrementTrains()
        {
            if (NbTrains > 0)
                NbTrains--;
        }

        public void AddEntrance(Signal signal)
        {
            Entrances.Add(signal);
        }

        public void AddExit(Signal signal)
        {
            Exits.Add(signal);
        }

        public Signal GetActiveExit()
        {
            foreach (var exit in Exits)
            {
                if (exit.Address == ActiveExitId)
                    return exit;
            }
            return null;
        }

        public void SetActiveExit(uint exitId)
        {
            ActiveExitId = exitId;
        }

        public void SetSpeed(int speed)
        {
            Speed = speed;
        }

        private Block GetNextBlock()
        {
            var activeExit = GetActiveExit();
            if (activeExit != null)
            {
                return activeExit.NextBlock;
            }
            return null;
        }

        /**
        * calculate the status of the signals based on the number of trains in the block and the next block
        */
        private void CalculateAndSendSignalStatuses()
        {
            foreach (var entrance in Entrances)
            {
                Status status;
                if (NbTrains > 0)
                {
                    status = Status.CLOSED;
                }
                else
                {
                    var nextBlock = GetNextBlock();
                    if (nextBlock != null && nextBlock.NbTrains > 0)
                    {
                        status = Status.WARNING;
                    }
                    else
                    {
                        status = Status.OPENED;
                    }
                }
                entrance.SetStatus(status);
            }
        }

        public void Update()
        {
            CalculateAndSendSignalStatuses();
        }
    }
}