// File: ToolShots.cs — Mẫu (không thuộc solution)
// Minh hoạ: docs/vision-tools/VISIONPRO_TOOLS.md — chụp ảnh màn hình editor THẬT của VisionPro
// cho từng tool trong các job mẫu, sau khi job đã chạy trên một ảnh (có LastRun + đồ hoạ).
//
//   ToolShots.exe <thư mục jobs> <file ảnh> <thư mục ảnh chụp>
//   ví dụ: ToolShots.exe jobs images\ok_03.bmp ..\..\docs\vision-tools\shots
//
// Mỗi job → <job>_toolblock.png (CogToolBlockEditV2) + <job>_<tool>.png (editor của tool).
// Editor tìm theo quy ước tên của VisionPro: Cog<Tên>Tool → Cog<Tên>EditV2 cùng namespace (vd.
// CogCaliperTool → Cognex.VisionPro.Caliper.CogCaliperEditV2), nên không cần liệt kê tay.
// Chụp bằng PrintWindow như docs/ui/spike/UiSpike.cs (control ActiveX vẽ đúng với cờ 2).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Cognex.VisionPro;
using Cognex.VisionPro.ImageFile;
using Cognex.VisionPro.ToolBlock;

internal static class ToolShots
{
    [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);

    private static readonly List<string> Log = new List<string>();

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length < 3)
        {
            Console.WriteLine("ToolShots.exe <jobs folder> <image file> <output folder>");
            return 2;
        }
        SetProcessDPIAware();
        Application.EnableVisualStyles();
        string outDir = args[2];
        Directory.CreateDirectory(outDir);
        LoadControlAssemblies();

        ICogImage image = LoadImage(args[1]);
        List<KeyValuePair<string, object>> shots = new List<KeyValuePair<string, object>>();
        List<CogToolBlock> blocks = new List<CogToolBlock>();
        foreach (string file in Directory.GetFiles(args[0], "*.vpp"))
        {
            string job = Path.GetFileNameWithoutExtension(file);
            CogToolBlock block = (CogToolBlock)CogSerializer.LoadObjectFromFile(file);
            // Mỗi job một ảnh riêng: Fixture của job sau không được sửa hệ "Fixture" job trước đã gắn
            // vào cùng ảnh (lỗi "modified a coordinate space that it did not create"); Copy() vẫn
            // dùng chung cây hệ toạ độ, constructor sao chép thì không (MeoVision DESIGN_LOG [31]).
            block.Inputs["InputImage"].Value = new CogImage8Grey((CogImage8Grey)image);
            block.Run();
            Log.Add(job + ": " + block.RunStatus.Result + " " + block.RunStatus.Message);
            blocks.Add(block);
            shots.Add(new KeyValuePair<string, object>(job + "_toolblock", block));
            foreach (ICogTool tool in block.Tools)
            {
                shots.Add(new KeyValuePair<string, object>(job + "_" + tool.Name, tool));
            }
        }

        Form form = new Form
        {
            Text = "ToolShots",
            StartPosition = FormStartPosition.Manual,
            Bounds = new Rectangle(0, 0, 1440, 820),
        };
        int index = -1;
        Control current = null;
        Timer timer = new Timer { Interval = 2500 };   // ActiveX cần thời gian vẽ lần đầu
        timer.Tick += (s, e) =>
        {
            if (current != null)
            {
                Snap(form, Path.Combine(outDir, shots[index].Key + ".png"));
                form.Controls.Remove(current);
                current.Dispose();
                current = null;
            }
            while (current == null && ++index < shots.Count)
            {
                current = NewEditor(shots[index].Value);
                if (current == null)
                {
                    Log.Add("no editor for " + shots[index].Key);
                }
            }
            if (current == null)
            {
                timer.Stop();
                File.WriteAllLines(Path.Combine(outDir, "toolshots_log.txt"), Log);
                foreach (CogToolBlock block in blocks)
                {
                    block.Dispose();
                }
                form.Close();
                return;
            }
            form.Text = shots[index].Key;
            current.Dock = DockStyle.Fill;
            form.Controls.Add(current);
        };
        form.Shown += (s, e) => timer.Start();
        Application.Run(form);
        return 0;
    }

    /// <summary>Editor V2 của tool theo quy ước tên; null nếu không có editor đó.</summary>
    private static Control NewEditor(object subject)
    {
        Type type = subject.GetType();
        string name = type.Name.EndsWith("Tool", StringComparison.Ordinal)
            ? type.Name.Substring(0, type.Name.Length - 4)
            : type.Name;
        string editorName = type.Namespace + "." + name + "EditV2";
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type editor = assembly.GetType(editorName, false);
            if (editor == null)
            {
                continue;
            }
            Control control = (Control)Activator.CreateInstance(editor);
            editor.GetProperty("Subject").SetValue(control, subject, null);
            Log.Add(editorName + " ← " + type.Name);
            return control;
        }
        return null;
    }

    /// <summary>
    /// Nạp sẵn mọi assembly *Controls của VisionPro (theo tên, nên lấy từ GAC giống lúc biên
    /// dịch) — editor nằm ở đó, exe này không tham chiếu trực tiếp từng cái.
    /// </summary>
    private static void LoadControlAssemblies()
    {
        string root = Environment.GetEnvironmentVariable("VPRO_ROOT") ??
            @"C:\Program Files\Cognex\VisionPro";
        string folder = Path.Combine(root, "ReferencedAssemblies");
        foreach (string file in Directory.GetFiles(folder, "Cognex.VisionPro*.Controls.dll"))
        {
            try
            {
                Assembly.Load(AssemblyName.GetAssemblyName(file));
            }
            catch (Exception)
            {
                try
                {
                    Assembly.LoadFrom(file);   // không có trong GAC: nạp thẳng từ thư mục
                }
                catch (Exception ex)
                {
                    Log.Add("skip " + Path.GetFileName(file) + ": " + ex.Message);
                }
            }
        }
    }

    private static void Snap(Control control, string file)
    {
        using (Bitmap bitmap = new Bitmap(control.Width, control.Height))
        {
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                IntPtr dc = g.GetHdc();
                PrintWindow(control.Handle, dc, 2);
                g.ReleaseHdc(dc);
            }
            bitmap.Save(file);
        }
        Log.Add("snap " + Path.GetFileName(file));
    }

    private static ICogImage LoadImage(string file)
    {
        CogImageFile reader = new CogImageFile();
        reader.Open(file, CogImageFileModeConstants.Read);
        try
        {
            return reader[0];
        }
        finally
        {
            reader.Close();
        }
    }
}
