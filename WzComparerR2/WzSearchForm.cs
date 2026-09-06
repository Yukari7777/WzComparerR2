using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using WzComparerR2.WzLib;

namespace WzComparerR2
{
    internal sealed class WzSearchForm : Form
    {
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly BindingList<WzSearchMatch> matches = new BindingList<WzSearchMatch>();
        private readonly Label status = new Label { AutoSize = true, Text = "검색 준비 중…" };
        private readonly Button cancel = new Button { Text = "중단", AutoSize = true };
        private readonly Button open = new Button { Text = "원본으로 이동", AutoSize = true, Enabled = false };
        private readonly DataGridView grid = new DataGridView
        {
            Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = false,
            AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false,
            RowHeadersVisible = false, ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText,
        };
        private readonly Wz_Node[] roots;
        private readonly WzSearchOptions options;
        private bool busy = true;
        public WzSearchMatch SelectedMatch { get; private set; }

        public WzSearchForm(IEnumerable<Wz_Node> roots, WzSearchOptions options)
        {
            this.roots = roots.ToArray();
            this.options = options;
            Text = "열린 WZ 전체 검색 — " + options.Query;
            Size = new Size(1100, 650);
            StartPosition = FormStartPosition.CenterParent;
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(6) };
            footer.Controls.AddRange(new Control[] { status, cancel, open });
            Controls.Add(grid);
            Controls.Add(footer);
            foreach (var column in new[] { ("Source", "파일", 180), ("Path", "경로", 410), ("Field", "일치 항목", 75), ("Value", "값", 370) })
                grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = column.Item1, HeaderText = column.Item2, Width = column.Item3, SortMode = DataGridViewColumnSortMode.NotSortable });
            grid.DataSource = matches;
            cancel.Click += (s, e) => { if (busy) cancellation.Cancel(); else Close(); };
            open.Click += (s, e) => OpenSelected();
            grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) OpenSelected(); };
            Shown += async (s, e) => await RunSearch();
            FormClosing += (s, e) => { if (busy) { cancellation.Cancel(); e.Cancel = true; } };
        }

        private async Task RunSearch()
        {
            var progress = new Progress<string>(text => { if (busy && !IsDisposed) status.Text = text; });
            IProgress<WzSearchMatch> found = new Progress<WzSearchMatch>(match =>
            {
                if (IsDisposed) return;
                matches.Add(match);
                open.Enabled = !busy && matches.Count > 0;
            });
            long lastProgress = 0;
            try
            {
                var result = await Task.Run(() => WzSearch.Search(roots, options, cancellation.Token, (nodes, matches) =>
                {
                    long now = Environment.TickCount & int.MaxValue;
                    if (now - lastProgress < 150) return;
                    lastProgress = now;
                    ((IProgress<string>)progress).Report($"{nodes:N0}개 노드 확인 / {matches:N0}건 발견");
                }, found.Report));
                status.Text = $"{result.Matches.Count:N0}건 / {result.VisitedNodes:N0}개 노드 / 오류 {result.ErrorCount}건" +
                    (result.Cancelled ? " (중단됨)" : result.Truncated ? $" (최대 {options.Limit}건, 검색어를 좁혀주세요)" : " (완료)");
                if (result.Errors.Count > 0)
                    MessageBox.Show(this, string.Join("\r\n", result.Errors.Take(10).Select(e => e.Path + ": " + e.Message)), "일부 WZ를 읽지 못했습니다");
            }
            catch (Exception ex)
            {
                status.Text = "검색 실패";
                MessageBox.Show(this, ex.Message, "WZ 검색", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                busy = false;
                open.Enabled = matches.Count > 0;
                cancel.Text = "닫기";
            }
        }

        private void OpenSelected()
        {
            if (busy || !(grid.CurrentRow?.DataBoundItem is WzSearchMatch match)) return;
            SelectedMatch = match;
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) cancellation.Dispose();
            base.Dispose(disposing);
        }
    }
}
