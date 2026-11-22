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
    /**
    * Represents a block of the railway.
    * Stored informations aren't 100% accurate
    */
    public class Block
    {
        public long Id { get; set; }
        public int Speed { get; set; }
        public int NbTrains{ get; set; }

        public DateTime LastUpdate;

        public Block(long id)
        {
            Id = id;
            Speed = 0;
            NbTrains = 0;
            LastUpdate = DateTime.FromFileTimeUtc(0); //set it to 0 so that the first update will be accepted
        }

        public string MessageFormat()
        {
            return $"{Id},{NbTrains},{Speed},{LastUpdate.ToFileTimeUtc()}";
        }
    }
}