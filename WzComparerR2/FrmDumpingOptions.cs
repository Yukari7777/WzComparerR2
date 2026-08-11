using System;
using System.Drawing;
using System.Windows.Forms;
using WzComparerR2.WzLib;

namespace WzComparerR2
{
    public class FrmDumpingOptions : Form
    {
        private readonly CheckBox chkDumpRaw;
        private readonly CheckBox chkDumpExternal;
        private readonly CheckBox chkLeaveReference;
        private readonly Button btnOk;
        private readonly Button btnCancel;

        public FrmDumpingOptions(DumpingOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            this.Options = options.Clone();

            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;
            this.ClientSize = new Size(410, 165);

            chkDumpRaw = new CheckBox()
            {
                AutoSize = true,
                Text = "리소스를 본문에 포함(Base64, 메모리 사용 매우 많음)",
                Location = new Point(20, 20),
            };

            chkDumpExternal = new CheckBox()
            {
                AutoSize = true,
                Text = "링크를 해석하고 리소스를 외부 파일로 저장",
                Location = new Point(20, 50),
            };

            chkLeaveReference = new CheckBox()
            {
                AutoSize = true,
                Text = "리소스 경로를 JSON/XML에 기록",
                Location = new Point(20, 80),
            };

            btnOk = new Button()
            {
                Text = "확인",
                DialogResult = DialogResult.OK,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                Size = new Size(80, 27),
                Location = new Point(this.ClientSize.Width - 180, this.ClientSize.Height - 45),
            };

            btnCancel = new Button()
            {
                Text = "취소",
                DialogResult = DialogResult.Cancel,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                Size = new Size(80, 27),
                Location = new Point(this.ClientSize.Width - 90, this.ClientSize.Height - 45),
            };

            this.Controls.AddRange(new Control[]
            {
                chkDumpRaw,
                chkDumpExternal,
                chkLeaveReference,
                btnOk,
                btnCancel,
            });

            this.AcceptButton = btnOk;
            this.CancelButton = btnCancel;

            chkDumpRaw.CheckedChanged += (sender, e) =>
            {
                if (chkDumpRaw.Checked)
                {
                    chkDumpExternal.Checked = false;
                }
            };
            chkDumpExternal.CheckedChanged += (sender, e) =>
            {
                if (chkDumpExternal.Checked)
                {
                    chkDumpRaw.Checked = false;
                }
                chkLeaveReference.Enabled = chkDumpExternal.Checked;
                if (!chkDumpExternal.Checked)
                {
                    chkLeaveReference.Checked = false;
                }
            };

            chkDumpRaw.Checked = this.Options.DumpRaw;
            chkDumpExternal.Checked = this.Options.DumpExternal;
            chkLeaveReference.Checked = this.Options.LeaveReference && chkDumpExternal.Checked;
            chkLeaveReference.Enabled = chkDumpExternal.Checked;

            this.Text = "내보내기 옵션";
        }

        public DumpingOptions Options { get; private set; }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (this.DialogResult == DialogResult.OK)
            {
                this.Options = new DumpingOptions
                {
                    DumpRaw = chkDumpRaw.Checked,
                    DumpExternal = chkDumpExternal.Checked,
                    LeaveReference = chkLeaveReference.Checked,
                };
            }

            base.OnFormClosing(e);
        }
    }
}
