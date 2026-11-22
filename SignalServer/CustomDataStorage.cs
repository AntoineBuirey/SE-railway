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

/* format of Me.CustomData:

property1=value1
property2=value2
property3=value3
...

*/

namespace IngameScript
{
    public class CustomDataStorage
    {
        private readonly Dictionary<string, string> configuration = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly IMyTerminalBlock Me;

        public CustomDataStorage(IMyTerminalBlock me)
        {
            Me = me;
            string[] lines = (Me.CustomData ?? string.Empty).Replace("\r", string.Empty).Split('\n');
            configuration.Clear();
            for (int i = 0; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                    continue;
                string line = lines[i].Trim();
                int separator = line.IndexOf('=');
                if (separator <= 0)
                    continue;

                string key = line.Substring(0, separator).Trim();
                string value = line.Substring(separator + 1).Trim();
                configuration[key] = value;
            }
        }

        public string Get(string key, string defaultValue = "", bool setIfNotFound = false)
        {
            string value;
            if (configuration.TryGetValue(key, out value))
            {
                return value;
            }
            if (setIfNotFound)
                configuration[key] = defaultValue;
            return defaultValue;
        }

        public int Get(string key, int defaultValue = 0, bool setIfNotFound = false)
        {
            string value;
            if (configuration.TryGetValue(key, out value))
            {
                int result;
                if (int.TryParse(value, out result))
                    return result;
            }
            if (setIfNotFound)
                configuration[key] = defaultValue.ToString();
            return defaultValue;
        }

        public bool Get(string key, bool defaultValue = false, bool setIfNotFound = false)
        {
            string value;
            if (configuration.TryGetValue(key, out value))
            {
                bool result;
                if (bool.TryParse(value, out result))
                    return result;
            }
            if (setIfNotFound)
                configuration[key] = defaultValue.ToString();
            return defaultValue;
        }

        public long Get(string key, long defaultValue = 0, bool setIfNotFound = false)
        {
            string value;
            if (configuration.TryGetValue(key, out value))
            {
                long result;
                if (long.TryParse(value, out result))
                    return result;
            }
            if (setIfNotFound)
                configuration[key] = defaultValue.ToString();
            return defaultValue;
        }
    }
}