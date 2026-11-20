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

        public CustomDataStorage()
        {
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

        /**
        * replace the value of "key" by "value" in "customData" and return "customData" with the new value.
        */
        private string EditDataValue(string customData, string key, string value)
        {
            string[] lines = customData.Split('\n');
            bool useCarriageReturn = customData.IndexOf("\r\n", StringComparison.Ordinal) >= 0;
            var result = new StringBuilder();
            bool found = false;

            for (int i = 0; i < lines.Length; i++)
            {
                // keep the line format as it is
                string line = lines[i];
                bool hasCarriageReturn = line.EndsWith("\r", StringComparison.Ordinal);
                string content = hasCarriageReturn ? line.Substring(0, line.Length - 1) : line;
                int separator = content.IndexOf('=');
                if (separator > 0 && content.Substring(0, separator).Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    result.Append(content.Substring(0, separator + 1)).Append(value);
                    if (hasCarriageReturn)
                        result.Append('\r');
                    found = true;
                }
                else
                {
                    result.Append(line);
                }

                if (i < lines.Length - 1)
                    result.Append('\n');
            }

            if (!found)
            {
                if (result.Length > 0 && result[result.Length - 1] != '\n')
                    result.Append(useCarriageReturn ? "\r\n" : "\n");
                result.Append(key).Append('=').Append(value);
            }

            return result.ToString();
        }

        public void Save()
        {
            string customData = Me.CustomData ?? string.Empty;
            foreach (var kv in configuration)
            {
                customData = EditDataValue(customData, kv.Key, kv.Value);
            }
            Me.CustomData = customData;
        }

        public string Get(string key, string defaultValue = "", bool setIfNotFound = false)
        {
            if (configuration.TryGetValue(key, out string value))
                return value;
            if (setIfNotFound)
                configuration[key] = defaultValue;
            return defaultValue;
        }

        public void Set(string key, string value)
        {
            configuration[key] = value;
        }
    }
}