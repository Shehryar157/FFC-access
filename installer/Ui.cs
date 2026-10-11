using System;
using System.Drawing;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace FFCAccessInstaller
{
    /// <summary>The player pressed Cancel and confirmed it.</summary>
    internal class CancelledException : Exception
    {
    }

    /// <summary>
    /// The installer's windows. They're made of Windows' standard parts (message boxes, buttons, an edit box, a
    /// progress bar), which screen readers read like any other dialog.
    /// </summary>
    internal static class Ui
    {
        public const string Title = "FFC Access";

        public const uint Ok = 0x0, OkCancel = 0x1, YesNo = 0x4, SecondButtonDefault = 0x100;
        public const uint IconError = 0x10, IconQuestion = 0x20, IconWarning = 0x30, IconInfo = 0x40;
        public const int IdOk = 1, IdYes = 6;

        /// <summary>--silent (testing): print messages instead of showing them, and answer yes.</summary>
        public static bool Silent;

        [DllImport("user32", CharSet = CharSet.Unicode)]
        private static extern int MessageBoxW(IntPtr owner, string text, string caption, uint flags);

        [DllImport("user32")]
        private static extern bool MessageBeep(uint type);

        /// <summary>A standard Windows message box, brought to the front. Returns the button pressed.</summary>
        public static int Message(string text, uint flags = Ok | IconInfo)
        {
            if (Silent)
            {
                Console.WriteLine(text);
                return (flags & YesNo) != 0 ? IdYes : IdOk;
            }
            return MessageBoxW(IntPtr.Zero, text, Title, flags | 0x10000);
        }

        /// <summary>The player's own Windows "Asterisk" sound: the job is done.</summary>
        public static void Alert()
        {
            if (!Silent)
            {
                MessageBeep(IconInfo);
            }
        }

        /// <summary>
        /// The "update found" dialog: the new version, what the player has now, what's new in a read-only text box
        /// (focus starts there, so a screen reader can arrow through it), and Update and Skip buttons.
        /// Enter = Update, Escape = Skip. True to update.
        /// </summary>
        public static bool AskUpdate(string version, string notes, string have)
        {
            using (Form form = NewForm("FFC Access update", 520, 360))
            {
                Label heading = new Label
                {
                    Text = "FFC Access " + version + " is available. " + have + " What's new:",
                    Location = new Point(12, 12),
                    Size = new Size(496, 20),
                    TabIndex = 3
                };
                TextBox box = new TextBox
                {
                    Multiline = true,
                    ReadOnly = true,
                    ScrollBars = ScrollBars.Vertical,
                    Location = new Point(12, 36),
                    Size = new Size(496, 270),
                    TabIndex = 0,
                    Text = string.Join("\r\n", (string.IsNullOrWhiteSpace(notes) ? "No notes were written for this version." : notes)
                        .Replace("\r\n", "\n").Split('\n'))
                };
                Button update = new Button { Text = "&Update", Location = new Point(312, 318), Size = new Size(94, 28), TabIndex = 1, DialogResult = DialogResult.OK };
                Button skip = new Button { Text = "&Skip", Location = new Point(414, 318), Size = new Size(94, 28), TabIndex = 2, DialogResult = DialogResult.Cancel };
                form.Controls.AddRange(new Control[] { heading, box, update, skip });
                form.AcceptButton = update;
                form.CancelButton = skip;
                form.Shown += (s, e) =>
                {
                    box.Select(0, 0); // start at the top, with nothing selected
                    form.Activate();
                };
                return form.ShowDialog() == DialogResult.OK;
            }
        }

        public static Form NewForm(string title, int width, int height)
        {
            return new Form
            {
                Text = title,
                ClientSize = new Size(width, height),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                ShowIcon = false,
                StartPosition = FormStartPosition.CenterScreen,
                Font = new Font("Segoe UI", 9f)
            };
        }
    }

    /// <summary>
    /// A window with a heading, a status line, a progress bar and Cancel, shown while a job runs on a second thread
    /// (the window has to keep answering Windows, or it freezes). The job reports with Update(fraction, status),
    /// calls Check() between steps (it waits there while "Stop?" is being asked, and throws CancelledException once
    /// Cancel is confirmed) and Finishing(status) before a last step that mustn't be interrupted: Cancel is greyed
    /// out from then on. The percentage is also in the window's title, so a screen reader's "read title" key gives
    /// it at any time.
    /// </summary>
    internal class Progress
    {
        private const int Steps = 1000;

        private readonly string title;
        private readonly string stopQuestion;
        private readonly object guard = new object();
        private readonly ManualResetEvent carryOn = new ManualResetEvent(true); // unset while asking "Stop?"
        private readonly DateTime started = DateTime.UtcNow;
        private double fraction;
        private string status = "Starting";
        private volatile bool cancellable = true, cancelled, asking, done;

        public Progress(string title, string stopQuestion)
        {
            this.title = title;
            this.stopQuestion = stopQuestion;
        }

        // ---------- called by the job (second thread) ----------

        public void Update(double? newFraction, string newStatus)
        {
            lock (guard)
            {
                if (newFraction.HasValue) fraction = Math.Min(Math.Max(newFraction.Value, 0), 1);
                if (newStatus != null)
                {
                    if (Ui.Silent && newStatus != status) Console.WriteLine(newStatus);
                    status = newStatus;
                }
            }
        }

        public void Check()
        {
            carryOn.WaitOne();
            if (cancelled) throw new CancelledException();
        }

        public void Finishing(string newStatus)
        {
            Check();
            cancellable = false;
            Update(null, newStatus);
        }

        // ---------- the window (main thread) ----------

        /// <summary>Show the window, run the job, close the window. Throws CancelledException, or the job's own error.</summary>
        public void Run(Action<Progress> job)
        {
            if (Ui.Silent)
            {
                job(this);
                return;
            }
            Exception error = null;
            Thread worker = new Thread(() =>
            {
                try { job(this); }
                catch (Exception e) { error = e; }
                done = true;
            }) { IsBackground = true };

            using (Form form = Ui.NewForm(title, 440, 132))
            using (Font bold = new Font("Segoe UI", 12f, FontStyle.Bold))
            using (System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 100 })
            {
                Label heading = new Label { Text = title, Font = bold, Location = new Point(14, 10), Size = new Size(412, 26) };
                Label line = new Label { Text = "Starting", Location = new Point(14, 42), Size = new Size(412, 18) };
                ProgressBar bar = new ProgressBar { Maximum = Steps, Location = new Point(14, 64), Size = new Size(412, 20), Style = ProgressBarStyle.Continuous };
                Button cancel = new Button { Text = "Cancel", Location = new Point(338, 94), Size = new Size(88, 28), TabIndex = 0 };
                form.Controls.AddRange(new Control[] { heading, line, bar, cancel });
                form.CancelButton = cancel;

                Action askStop = () =>
                {
                    if (!cancellable || asking || cancelled || done) return;
                    asking = true;
                    carryOn.Reset(); // the job waits at its next Check()
                    DialogResult answer = MessageBox.Show(form, stopQuestion, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    asking = false;
                    if (answer == DialogResult.Yes)
                    {
                        cancelled = true;
                        Update(null, "Cancelling");
                    }
                    carryOn.Set();
                };
                cancel.Click += (s, e) => askStop();
                form.FormClosing += (s, e) =>
                {
                    if (!done) // Alt+F4 or the close button: same as Cancel
                    {
                        e.Cancel = true;
                        askStop();
                    }
                };
                int shownPercent = -1;
                string shownStatus = null;
                timer.Tick += (s, e) =>
                {
                    if (asking) return;
                    if (done)
                    {
                        timer.Stop();
                        form.Close();
                        return;
                    }
                    double f;
                    string text;
                    lock (guard)
                    {
                        f = fraction;
                        text = status + TimeLeft(fraction);
                    }
                    bar.Value = (int)(f * Steps);
                    if (text != shownStatus) line.Text = shownStatus = text;
                    int percent = (int)(f * 100);
                    if (percent != shownPercent)
                    {
                        shownPercent = percent;
                        form.Text = percent + "% - " + title;
                    }
                    if (!cancellable && cancel.Enabled) cancel.Enabled = false;
                };
                form.Shown += (s, e) =>
                {
                    form.Activate();
                    timer.Start();
                    worker.Start();
                };
                form.ShowDialog();
            }
            if (error != null)
            {
                ExceptionDispatchInfo.Capture(error).Throw(); // the job's own error, as it was thrown
            }
        }

        /// <summary>", about 10 seconds left" once there's enough to go on, else "".</summary>
        private string TimeLeft(double f)
        {
            double elapsed = (DateTime.UtcNow - started).TotalSeconds;
            if (cancelled || !cancellable || elapsed < 1.5 || f <= 0.03 || f >= 1) return "";
            double left = elapsed * (1 - f) / f;
            if (left < 3) return "";
            if (left < 60) return ", about " + ((int)(left / 5) + 1) * 5 + " seconds left"; // in steps of 5: calmer
            int minutes = (int)Math.Round(left / 60);
            return ", about " + minutes + " minute" + (minutes != 1 ? "s" : "") + " left";
        }
    }
}
