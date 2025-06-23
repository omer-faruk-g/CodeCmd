using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CodeCmdApp
{
    public partial class MainForm : Form
    {
        // Komut sistemleri:
        private Dictionary<string, Action<string[]>> commands = new Dictionary<string, Action<string[]>>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, string> aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, string> customCommands = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Loglama için in-memory yapı ve dosya:
        private List<(DateTime time, string message)> inMemoryLogs = new List<(DateTime, string)>();
        private string logFolder;
        private DateTime programStartTime;

        public MainForm()
        {
            InitializeComponent();
            InitializeUI();
            InitializeLogger();
            InitializeCommands();
            AppendOutput($"CodeCmd başlatıldı: {programStartTime:yyyy-MM-dd HH:mm:ss}");
        }

        #region UI Oluşturma

        private RichTextBox outputBox;
        private TextBox inputBox;

        private void InitializeUI()
        {
            // Form temel ayarları
            this.Text = "CodeCmd";
            this.Width = 800;
            this.Height = 600;

            // Output alanı: RichTextBox
            outputBox = new RichTextBox()
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                Font = new System.Drawing.Font("Consolas", 10),
            };
            this.Controls.Add(outputBox);

            // Input alanı: TextBox, altta
            inputBox = new TextBox()
            {
                Dock = DockStyle.Bottom,
                Font = new System.Drawing.Font("Consolas", 10),
            };
            inputBox.KeyDown += InputBox_KeyDown;
            this.Controls.Add(inputBox);

            // Form kapatılırken log kaydetme vb. işlemler
            this.FormClosing += MainForm_FormClosing;
        }

        private void InputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true; // disallow ding sesi
                string input = inputBox.Text.Trim();
                if (!string.IsNullOrEmpty(input))
                {
                    AppendOutput($"> {input}");
                    Logger_WriteLog(input);
                    ExecuteCommand(input);
                }
                inputBox.Clear();
            }
        }

        private void AppendOutput(string text)
        {
            if (InvokeRequired)
            {
                this.Invoke(new Action(() => AppendOutput(text)));
                return;
            }
            outputBox.AppendText(text + Environment.NewLine);
            outputBox.SelectionStart = outputBox.Text.Length;
            outputBox.ScrollToCaret();
        }

        #endregion

        #region Logger

        private void InitializeLogger()
        {
            programStartTime = DateTime.Now;
            logFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            if (!Directory.Exists(logFolder))
                Directory.CreateDirectory(logFolder);
            // Başlangıçta bir günlük dosyası açalım:
            string todayLogFile = GetLogFilePath(programStartTime);
            // Dosyayı oluştur veya aç:
            try
            {
                if (!File.Exists(todayLogFile))
                    File.Create(todayLogFile).Dispose();
            }
            catch { /* ignore */ }
        }

        private string GetLogFilePath(DateTime time)
        {
            // logs_yyyy-MM-dd.txt
            string fileName = $"logs_{time:yyyy-MM-dd}.txt";
            return Path.Combine(logFolder, fileName);
        }

        private void Logger_WriteLog(string message)
        {
            DateTime now = DateTime.Now;
            inMemoryLogs.Add((now, message));

            // Dosyaya da ekle
            string logPath = GetLogFilePath(programStartTime);
            string entry = $"[{now:yyyy-MM-dd HH:mm:ss}] {message}";
            try
            {
                File.AppendAllText(logPath, entry + Environment.NewLine);
            }
            catch
            {
                // Hata yırt, in-memory tutuyoruz en azından
            }
        }

        private List<(DateTime time, string message)> GetLogsInRange(DateTime from, DateTime to)
        {
            return inMemoryLogs.Where(t => t.time >= from && t.time <= to).ToList();
        }

        #endregion

        #region Komut Sistemi

        private void InitializeCommands()
        {
            // Temel komutlar:
            commands["help"] = args => Cmd_Help(args);
            commands["exit"] = args => Cmd_Exit(args);
            commands["reload"] = args => Cmd_Reload(args);
            commands["start"] = args => Cmd_Start(args);
            commands["log"] = args => Cmd_Log(args);
            commands["search"] = args => Cmd_Search(args);
            commands["give"] = args => Cmd_Give(args);
            commands["assign"] = args => Cmd_Assign(args);
            // İleride başka komut eklemek istersen buraya...
        }

        private void ExecuteCommand(string input)
        {
            // Basit parse: boşlukla böl, fakat give vs. için action kısmını birleştireceğiz
            string[] parts = input.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return;

            string cmd = parts[0];

            // Alias kontrolü
            if (aliases.TryGetValue(cmd, out string realCmd))
            {
                cmd = realCmd;
            }

            string rest = parts.Length > 1 ? parts[1] : "";

            // Komut mevcut mu?
            if (commands.ContainsKey(cmd))
            {
                // Komutun paramlarını parçalayıp çağır. Bazı komutlar kendi içinde daha detay parse edebilir.
                string[] args = ParseArgs(rest);
                try
                {
                    commands[cmd].Invoke(args);
                }
                catch (Exception ex)
                {
                    AppendOutput($"Komut çalıştırma hatası: {ex.Message}");
                }
            }
            else if (customCommands.ContainsKey(cmd))
            {
                // customCommands[cmd] => mapped komut string, tekrar ExecuteCommand
                string mapped = customCommands[cmd];
                AppendOutput($"(Custom) `{cmd}` → `{mapped}` olarak çalıştırılıyor");
                ExecuteCommand(mapped);
            }
            else
            {
                AppendOutput($"Bilinmeyen komut: {cmd}");
            }
        }

        private string[] ParseArgs(string rest)
        {
            if (string.IsNullOrWhiteSpace(rest))
                return new string[0];
            // Basit: boşlukla ayır. İleri seviye için tırnak içi vs. parse ekleyebilirsin.
            return rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        }

        #endregion

        #region Komut Implementasyonları

        private void Cmd_Help(string[] args)
        {
            AppendOutput("Mevcut komutlar:");
            // Standart komut listesi ve kısa açıklama:
            AppendOutput("  help                           : Tüm komutları ve kısa açıklamalarını gösterir.");
            AppendOutput("  start <dosyaAdı>               : .js, .py, .java, .html, .css, .cpp vb. dosyayı sistemde kayıtlı programla açar.");
            AppendOutput("  log [HH:mm-HH:mm] veya logHH  : Program açılışından bu yana veya belirtilen zaman aralığındaki logları gösterir.");
            AppendOutput("     Örnek: log                 -> Başlangıçtan şu ana kadar logları gösterir.");
            AppendOutput("     Örnek: log 08:00-21:00     -> Bugün 08:00-21:00 arasındaki logları gösterir.");
            AppendOutput("     Örnek: log13               -> Bugün saat 13:00-13:59 arasındaki logları gösterir.");
            AppendOutput("  reload                         : Shell motorunu yeniden başlatır.");
            AppendOutput("  exit                           : Uygulamayı kapatır.");
            AppendOutput("  search <dosyaAdı>              : Çalışma dizini ve alt klasörlerinde dosyayı arar, bulursa açar.");
            AppendOutput("  give <komutAdı> <işlevKomutu>  : Özel komut tanımlar. KomutAdı girilince İşlevKomutu çalışır.");
            AppendOutput("     Örnek: give greet echo Merhaba");
            AppendOutput("  assign <varolan> <alias>       : Varolan komuta yeni takma isim ekler.");
            AppendOutput("     Örnek: assign help llp      -> 'llp' yazınca help komutu çalışır.");
            // Dinamik eklenen custom komutlar:
            if (customCommands.Count > 0)
            {
                AppendOutput("  -- Custom Komutlar --");
                foreach (var kv in customCommands)
                {
                    AppendOutput($"     {kv.Key} => `{kv.Value}`");
                }
            }
            // Aliaslar:
            if (aliases.Count > 0)
            {
                AppendOutput("  -- Aliaslar --");
                foreach (var kv in aliases)
                {
                    AppendOutput($"     {kv.Key} => {kv.Value}");
                }
            }
        }

        private void Cmd_Exit(string[] args)
        {
            AppendOutput("Çıkılıyor...");
            Application.Exit();
        }

        private void Cmd_Reload(string[] args)
        {
            AppendOutput("Yeniden başlatılıyor...");
            Application.Restart();
        }

        private void Cmd_Start(string[] args)
        {
            if (args.Length < 1)
            {
                AppendOutput("Kullanım: start <dosyaAdı>");
                return;
            }
            string fileName = args[0];
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    UseShellExecute = true
                };
                Process.Start(psi);
                AppendOutput($"Start: {fileName} açılmaya çalışıldı.");
            }
            catch (Exception ex)
            {
                AppendOutput($"Start hatası: {ex.Message}");
            }
        }

        private void Cmd_Log(string[] args)
        {
            // args boş ise: başlangıçtan bugüne kadar tüm logları göster.
            // args[0] formatı: "HH:mm-HH:mm" veya "HH-mm" veya "HH" (ör: 13 → 13:00-13:59)
            DateTime today = DateTime.Now.Date;
            DateTime from = programStartTime;
            DateTime to = DateTime.Now;

            if (args.Length >= 1)
            {
                string p = args[0];
                // "log13" gibi doğrudan "13" olarak parse edilip gelmiş de olabilir; 
                // Eğer kullanıcı “log13” yazdıysa, args[0] = "13".
                // Eğer “log 08:00-21:00” ise args[0] = "08:00-21:00".
                if (p.Contains("-"))
                {
                    // "HH:mm-HH:mm" veya "HH-HH"
                    string[] parts = p.Split('-', 2);
                    if (parts.Length == 2)
                    {
                        if (TryParseTime(parts[0], out TimeSpan ts1) && TryParseTime(parts[1], out TimeSpan ts2))
                        {
                            from = today + ts1;
                            to = today + ts2;
                        }
                        else
                        {
                            AppendOutput("Zaman aralığı parse edilemedi. Örnek: log 08:00-21:00 veya log 8-21 veya log13");
                            return;
                        }
                    }
                }
                else
                {
                    // Tek sayı veya "13" ya da "08:00" gibi
                    if (TryParseTime(p, out TimeSpan singleTs))
                    {
                        from = today + singleTs;
                        to = today + singleTs.Add(TimeSpan.FromHours(1)).Add(TimeSpan.FromSeconds(-1)); // o saat içi
                    }
                    else
                    {
                        AppendOutput("Zaman parse edilemedi. Örnek: log13 veya log 13 veya log 13:00");
                        return;
                    }
                }
            }

            // Filtrele ve göster:
            var entries = GetLogsInRange(from, to);
            if (entries.Count == 0)
            {
                AppendOutput($"[{from:HH:mm} - {to:HH:mm}] aralığında kayıt bulunamadı.");
            }
            else
            {
                AppendOutput($"--- [{from:HH:mm} - {to:HH:mm}] log kayıtları ---");
                foreach (var e in entries)
                {
                    AppendOutput($"[{e.time:HH:mm:ss}] {e.message}");
                }
                AppendOutput($"--- Toplam {entries.Count} kayıt ---");
            }
        }

        private bool TryParseTime(string s, out TimeSpan ts)
        {
            ts = default;
            // "13" → 13:00, "08:00" → 08:00
            if (int.TryParse(s, out int hour) && hour >= 0 && hour < 24)
            {
                ts = TimeSpan.FromHours(hour);
                return true;
            }
            if (TimeSpan.TryParse(s, out ts))
            {
                // accepted format HH:mm
                return true;
            }
            return false;
        }

        private void Cmd_Search(string[] args)
        {
            if (args.Length < 1)
            {
                AppendOutput("Kullanım: search <dosyaAdı>");
                return;
            }
            string targetName = args[0];
            AppendOutput($"Arama başlatılıyor: {targetName} (Çalışma dizininde ve alt dizinlerde)");

            // UI kilitlenmesin diye asenkron çalıştır:
            Task.Run(() =>
            {
                string startDir = Environment.CurrentDirectory;
                bool foundAny = false;
                try
                {
                    // Recursive olarak ara, ama UnauthorizedAccess vb. hatalar olabilir:
                    foreach (string dir in SafeEnumerateDirectories(startDir))
                    {
                        try
                        {
                            foreach (string file in Directory.GetFiles(dir))
                            {
                                string fileName = Path.GetFileName(file);
                                if (fileName.Equals(targetName, StringComparison.OrdinalIgnoreCase))
                                {
                                    foundAny = true;
                                    // UI thread’e bildir:
                                    AppendOutput($"Bulundu: {file}");
                                    // Açmayı dener:
                                    try
                                    {
                                        Process.Start(new ProcessStartInfo
                                        {
                                            FileName = file,
                                            UseShellExecute = true
                                        });
                                        AppendOutput($"Açıldı: {file}");
                                    }
                                    catch (Exception ex2)
                                    {
                                        AppendOutput($"AÇMA Hatası: {ex2.Message}");
                                    }
                                    // Eğer birden fazla aramak istersen continue; yoksa break. Burada ilkini açıp bırakmak için break:
                                    return;
                                }
                            }
                        }
                        catch { /* klasöre erişilemedi, atla */ }
                    }
                }
                catch { /* root erişim hatası vb */ }

                if (!foundAny)
                {
                    AppendOutput($"Dosya bulunamadı: {targetName}");
                }
            });
        }

        private IEnumerable<string> SafeEnumerateDirectories(string root)
        {
            var dirs = new Stack<string>();
            dirs.Push(root);
            while (dirs.Count > 0)
            {
                string current = dirs.Pop();
                yield return current;
                try
                {
                    foreach (var sub in Directory.GetDirectories(current))
                    {
                        dirs.Push(sub);
                    }
                }
                catch
                {
                    // erişim yoksa atla
                }
            }
        }

        private void Cmd_Give(string[] args)
        {
            // Kullanım: give <komutAdı> <işlevKomutu>
            if (args.Length < 2)
            {
                AppendOutput("Kullanım: give <komutAdı> <işlevKomutu>");
                return;
            }
            string name = args[0];
            // args[1] ve sonrası birleşik: aslında ParseArgs ile split edilmiş. 
            // Burada rest stringine erişmek istesek ExecuteCommand'ta ikinci parametreyi ayırmamız gerekecek.
            // Ancak şu an args[1] sadece ilk parça. Bu yüzden ExecuteCommand içinde ParseArgs yerine:
            // parts = input.Split(' ', 3) şeklinde parse edilse daha iyi olur. 
            // Basit: customCommands'a kaydederken orijinal input'u kaydedebiliriz. 
            // Ama şu an args dizisi tek kelime kalmış olabilir. 
            // Güvenilir olması için ExecuteCommand'da parts = input.Split(' ', 3) yapılmalı. 
            // Bu örnekte, varsayalım ExecuteCommand bunu destekliyor: rest parametresi tamam burada.
            // Burada args[1] ve sonrası birleştir:
            string action = string.Join(' ', args.Skip(1));
            customCommands[name] = action;
            AppendOutput($"Custom komut eklendi: {name} => `{action}`");
        }

        private void Cmd_Assign(string[] args)
        {
            // Kullanım: assign <varolan> <alias>
            if (args.Length < 2)
            {
                AppendOutput("Kullanım: assign <varolanKomut> <alias>");
                return;
            }
            string existing = args[0];
            string alias = args[1];
            // varolan komutun gerçekten var olup olmadığı kontrol edilebilir:
            if (!commands.ContainsKey(existing) && !customCommands.ContainsKey(existing))
            {
                AppendOutput($"Mevcut komut bulunamadı: {existing}");
                return;
            }
            aliases[alias] = existing;
            AppendOutput($"Alias eklendi: {alias} => {existing}");
        }

        #endregion

        #region Form Closing

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            AppendOutput($"CodeCmd kapanıyor: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            // İstersen kapanışta log dosyasına kapanış zamanını da yaz:
            try
            {
                string logPath = GetLogFilePath(programStartTime);
                string entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Program kapandı.";
                File.AppendAllText(logPath, entry + Environment.NewLine);
            }
            catch { }
        }

        #endregion
    }
}
