using System;
using System.Windows.Forms;
using WzComparerR2.WzLib;

namespace WzComparerR2
{
    public partial class MainForm
    {
        private readonly object openWzSearchCriterion = new DevComponents.Editors.ComboItem { Text = "열린 WZ 전체" };

        private void SearchAllOpenWz()
        {
            try
            {
                var options = new WzSearchOptions
                {
                    Query = textBoxItemSearchWz.Text,
                    Mode = checkBoxItemRegex1.Checked ? "regex" : checkBoxItemExact1.Checked ? "exact" : "contains",
                };
                using (var dialog = new WzSearchForm(WzSearch.OpenRoots(openedWz), options))
                {
                    if (dialog.ShowDialog(this) == DialogResult.OK && dialog.SelectedMatch != null)
                    {
                        var node = dialog.SelectedMatch.ResolveNode();
                        if (node == null || !OnSelectedWzNode(node))
                            MessageBox.Show(this, "검색 결과의 원본 노드를 찾을 수 없습니다.");
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "WZ 검색", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
