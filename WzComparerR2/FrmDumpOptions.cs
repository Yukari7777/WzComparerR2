using System.Drawing;
using System.Windows.Forms;
using DevComponents.DotNetBar;
using WzComparerR2.WzLib;

namespace WzComparerR2
{
    public sealed class FrmDumpOptions : Office2007Form
    {
        private readonly CheckBox chkDumpRaw;
        private readonly CheckBox chkDumpExternal;
        private readonly CheckBox chkLeaveReference;
        private readonly Button btnOk;
        private readonly Button btnCancel;

        public FrmDumpOptions(string title, WzDumpOptions defaults)
        {
            Text = string.IsNullOrEmpty(title) ? "내보내기 옵션" : title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ShowIcon = false;
            ClientSize = new Size(340, 150);
            AutoScaleMode = AutoScaleMode.Font;

            chkDumpRaw = new CheckBox()
            {
                AutoSize = true,
                Location = new Point(20, 20),
                Text = "원본 데이터를 Base64로 포함",
            };
            chkDumpRaw.CheckedChanged += (_, __) => UpdateControlStates();

            chkDumpExternal = new CheckBox()
            {
                AutoSize = true,
                Location = new Point(20, 55),
                Text = "리소스를 외부 파일로 저장",
            };
            chkDumpExternal.CheckedChanged += (_, __) => UpdateControlStates();

            chkLeaveReference = new CheckBox()
            {
                AutoSize = true,
                Location = new Point(40, 90),
                Text = "JSON/XML에 외부 파일 경로 포함",
            };

            btnOk = new Button()
            {
                Text = "확인",
                DialogResult = DialogResult.OK,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                Size = new Size(90, 27),
                Location = new Point(ClientSize.Width - 190, ClientSize.Height - 45),
            };
            btnOk.Click += (_, __) => Close();

            btnCancel = new Button()
            {
                Text = "취소",
                DialogResult = DialogResult.Cancel,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                Size = new Size(90, 27),
                Location = new Point(ClientSize.Width - 95, ClientSize.Height - 45),
            };

            Controls.Add(chkDumpRaw);
            Controls.Add(chkDumpExternal);
            Controls.Add(chkLeaveReference);
            Controls.Add(btnOk);
            Controls.Add(btnCancel);

            AcceptButton = btnOk;
            CancelButton = btnCancel;

            ApplyDefaults(defaults);
        }

        public WzDumpOptions SelectedOptions
        {
            get
            {
                return new WzDumpOptions
                {
                    DumpRaw = chkDumpRaw.Checked,
                    DumpExternal = chkDumpExternal.Checked,
                    LeaveReference = chkLeaveReference.Checked,
                };
            }
        }

        private void ApplyDefaults(WzDumpOptions defaults)
        {
            var options = defaults ?? WzDumpOptions.CreateDefaults(false);
            chkDumpRaw.Checked = options.DumpRaw;
            chkDumpExternal.Checked = options.DumpExternal;
            chkLeaveReference.Checked = options.LeaveReference;
            UpdateControlStates();
        }

        private void UpdateControlStates()
        {
            if (chkDumpRaw.Checked)
            {
                chkDumpExternal.Checked = false;
            }

            chkDumpExternal.Enabled = !chkDumpRaw.Checked;
            chkLeaveReference.Enabled = chkDumpExternal.Checked && !chkDumpRaw.Checked;
            if (!chkLeaveReference.Enabled)
            {
                chkLeaveReference.Checked = false;
            }
        }
    }
}
