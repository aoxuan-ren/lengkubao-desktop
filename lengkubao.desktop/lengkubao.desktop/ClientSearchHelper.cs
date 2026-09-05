using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace lengkubao.desktop
{
    /// <summary>
    /// 可搜索 ComboBox 的绑定与过滤逻辑（客户/规格/库位等通用）。
    /// </summary>
    public static class ClientSearchHelper
    {
        public const string PlaceholderText = "输入编号/名称/首字母";
        public const string NamePlaceholderText = "输入名称/首字母";
        public const string NoneFilterCode = "__NONE__";
        public const string NoneFilterName = "无";

        public static ClientSearchItem CreateNoneFilterItem()
        {
            return new ClientSearchItem
            {
                Code = NoneFilterCode,
                Name = NoneFilterName,
                DisplayText = NoneFilterName,
                Initials = PinyinHelper.GetInitials(NoneFilterName)
            };
        }

        /// <summary>
        /// 「无」表示该筛选项不生效（等同未选择，查询全部）。
        /// </summary>
        public static bool IsNoneFilter(ClientSearchItem item)
        {
            return item != null && string.Equals(item.Code, NoneFilterCode, StringComparison.Ordinal);
        }

        /// <summary>文本为「无」或空白时，该筛选项不限制结果。</summary>
        public static bool IsNoRestrictionFilterText(string text)
        {
            return string.IsNullOrWhiteSpace(text)
                || string.Equals(text.Trim(), NoneFilterName, StringComparison.Ordinal);
        }

        public static void PrependNoneFilterItem(List<ClientSearchItem> items)
        {
            if (items == null)
                return;
            items.Insert(0, CreateNoneFilterItem());
        }

        public static List<ClientSearchItem> BuildSimpleItemsWithNone(IEnumerable<string> names)
        {
            var items = BuildSimpleItems(names);
            PrependNoneFilterItem(items);
            return items;
        }

        public static List<ClientSearchItem> BuildItemsWithNone(DataTable clients)
        {
            var items = BuildItems(clients);
            PrependNoneFilterItem(items);
            return items;
        }

        public static void AppendClientFilter(
            ClientSearchItem item,
            List<string> conditions,
            Dictionary<string, object> parameters)
        {
            if (item == null)
                return;

            if (IsNoneFilter(item))
                return;

            if (!string.IsNullOrEmpty(item.Code))
            {
                conditions.Add("client_code = @clientCode");
                parameters["@clientCode"] = item.Code;
            }
            else
            {
                conditions.Add("client_name = @clientName");
                parameters["@clientName"] = item.Name;
            }
        }

        public static void AppendFieldFilter(
            ClientSearchItem item,
            string column,
            List<string> conditions,
            Dictionary<string, object> parameters,
            string paramName)
        {
            if (item == null || string.IsNullOrEmpty(column))
                return;

            if (IsNoneFilter(item))
                return;

            conditions.Add($"{column} = {paramName}");
            parameters[paramName] = item.Name;
        }

        public static string BuildClientCondition(
            ClientSearchItem item,
            string clientCodeColumn,
            string clientNameColumn,
            Func<string, string> escape)
        {
            if (item == null)
                return null;

            if (IsNoneFilter(item))
                return null;

            if (!string.IsNullOrEmpty(item.Code) && !string.IsNullOrEmpty(clientCodeColumn))
                return $"{clientCodeColumn} = '{escape(item.Code)}'";

            if (!string.IsNullOrEmpty(clientNameColumn))
                return $"{clientNameColumn} = '{escape(item.Name)}'";

            return null;
        }

        public static string BuildFieldCondition(
            ClientSearchItem item,
            string column,
            Func<string, string> escape)
        {
            if (item == null || string.IsNullOrEmpty(column))
                return null;

            if (IsNoneFilter(item))
                return null;

            return $"{column} = '{escape(item.Name)}'";
        }

        /// <summary>将查询结果中的 distinct 值合并进筛选项（保留首项「无」）。</summary>
        public static void MergeDistinctFromDataTable(
            List<ClientSearchItem> items,
            DataTable data,
            params string[] columnNames)
        {
            if (items == null || data == null || columnNames == null || columnNames.Length == 0)
                return;

            var existing = new HashSet<string>(
                items.Where(i => !IsNoneFilter(i)).Select(i => i.Name),
                StringComparer.OrdinalIgnoreCase);

            foreach (string columnName in columnNames)
            {
                if (!data.Columns.Contains(columnName))
                    continue;

                foreach (DataRow row in data.Rows)
                {
                    string value = row[columnName]?.ToString()?.Trim();
                    if (string.IsNullOrEmpty(value) || existing.Contains(value))
                        continue;

                    existing.Add(value);
                    items.Add(new ClientSearchItem
                    {
                        Code = value,
                        Name = value,
                        DisplayText = value,
                        Initials = PinyinHelper.GetInitials(value)
                    });
                }
            }

            if (items.Count <= 1)
                return;

            var noneItem = items[0];
            var sorted = items.Skip(1).OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
            items.Clear();
            items.Add(noneItem);
            items.AddRange(sorted);
        }

        /// <summary>从单据表加载 distinct 筛选项。</summary>
        public static List<ClientSearchItem> BuildDistinctFilterItems(
            IEnumerable<string> values)
        {
            return BuildSimpleItemsWithNone(values ?? Enumerable.Empty<string>());
        }

        public class SearchComboOptions
        {
            public string PlaceholderText { get; set; } = ClientSearchHelper.PlaceholderText;
            public bool AllowEmptySelection { get; set; }
            public Action<ClientSearchItem> OnSelected { get; set; }
        }

        public static void ApplySearchableStyle(ComboBox combo, int dropDownWidth = 300)
        {
            if (combo == null)
                return;

            combo.DropDownStyle = ComboBoxStyle.DropDown;
            combo.AutoCompleteMode = AutoCompleteMode.None;
            combo.DropDownWidth = dropDownWidth;
            combo.MaxDropDownItems = 20;
        }

        public static List<ClientSearchItem> BuildItems(DataTable clients)
        {
            var items = new List<ClientSearchItem>();
            if (clients == null)
                return items;

            foreach (DataRow row in clients.Rows)
            {
                string code = row["code"]?.ToString()?.Trim() ?? string.Empty;
                string name = row["name"]?.ToString()?.Trim() ?? string.Empty;
                if (string.IsNullOrEmpty(code) && string.IsNullOrEmpty(name))
                    continue;

                items.Add(new ClientSearchItem
                {
                    Code = code,
                    Name = name,
                    DisplayText = $"{name} ({code})",
                    Initials = PinyinHelper.GetInitials(name)
                });
            }

            return items.OrderBy(i => i.Code, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static List<ClientSearchItem> BuildSimpleItems(IEnumerable<string> names)
        {
            var items = new List<ClientSearchItem>();
            if (names == null)
                return items;

            foreach (string name in names)
            {
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                string n = name.Trim();
                items.Add(new ClientSearchItem
                {
                    Code = n,
                    Name = n,
                    DisplayText = n,
                    Initials = PinyinHelper.GetInitials(n)
                });
            }

            return items;
        }

        public static bool MatchesClientFields(string code, string name, string keyword, string placeholderText = null)
        {
            string clientName = name ?? string.Empty;
            return Matches(new ClientSearchItem
            {
                Code = code ?? string.Empty,
                Name = clientName,
                Initials = PinyinHelper.GetInitials(clientName)
            }, keyword, placeholderText);
        }

        public static bool Matches(ClientSearchItem item, string keyword, string placeholderText = null)
        {
            if (item == null)
                return false;

            string ph = placeholderText ?? PlaceholderText;
            if (string.IsNullOrWhiteSpace(keyword) || keyword == ph || keyword == PlaceholderText || keyword == NamePlaceholderText
                || string.Equals(keyword.Trim(), NoneFilterName, StringComparison.Ordinal))
                return true;

            string key = keyword.Trim();
            string keyLower = key.ToLowerInvariant();

            if (item.Code != null && item.Code.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (item.Name != null && item.Name.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            string initials = item.Initials;
            if (string.IsNullOrEmpty(initials) && !string.IsNullOrEmpty(item.Name))
                initials = PinyinHelper.GetInitials(item.Name);

            if (!string.IsNullOrEmpty(initials))
            {
                if (initials.StartsWith(keyLower, StringComparison.Ordinal))
                    return true;
                if (initials.IndexOf(keyLower, StringComparison.Ordinal) >= 0)
                    return true;
            }

            return false;
        }

        public static List<ClientSearchItem> Filter(List<ClientSearchItem> allItems, string keyword, string placeholderText = null)
        {
            if (allItems == null)
                return new List<ClientSearchItem>();

            string ph = placeholderText ?? PlaceholderText;
            if (string.IsNullOrWhiteSpace(keyword) || keyword == ph || keyword == PlaceholderText || keyword == NamePlaceholderText
                || string.Equals(keyword.Trim(), NoneFilterName, StringComparison.Ordinal))
                return allItems.ToList();

            return allItems.Where(i => Matches(i, keyword, ph)).ToList();
        }

        public static void BindSearchableCombo(ComboBox combo, List<ClientSearchItem> allItems)
        {
            BindSearchableCombo(combo, allItems, (SearchComboOptions)null);
        }

        public static void BindSearchableCombo(
            ComboBox combo,
            List<ClientSearchItem> allItems,
            Action<ClientSearchItem> onSelected)
        {
            BindSearchableCombo(combo, allItems, new SearchComboOptions { OnSelected = onSelected });
        }

        public static void BindSearchableCombo(
            ComboBox combo,
            List<ClientSearchItem> allItems,
            SearchComboOptions options)
        {
            if (combo == null)
                return;

            options = options ?? new SearchComboOptions();

            var existing = combo.Tag as ComboSearchState;
            if (existing != null)
            {
                var previousSelection = existing.SelectedItem;
                string previousEffectiveText = GetEffectiveText(combo, existing);

                existing.AllItems = allItems ?? new List<ClientSearchItem>();
                existing.PlaceholderText = options.PlaceholderText ?? PlaceholderText;
                existing.AllowEmptySelection = options.AllowEmptySelection;
                existing.OnSelected = options.OnSelected;
                ApplySearchableStyle(combo);

                if (previousSelection != null)
                {
                    var restored = FindMatchingItem(existing.AllItems, previousSelection);
                    if (restored != null)
                    {
                        existing.SelectedItem = restored;
                        SetSelectedItem(combo, existing, restored);
                        return;
                    }
                }

                if (!string.IsNullOrWhiteSpace(previousEffectiveText))
                {
                    if (IsNoRestrictionFilterText(previousEffectiveText))
                    {
                        var noneItem = existing.AllItems.FirstOrDefault(IsNoneFilter);
                        if (noneItem != null)
                        {
                            existing.SelectedItem = noneItem;
                            SetSelectedItem(combo, existing, noneItem);
                            return;
                        }
                    }

                    existing.SelectedItem = null;
                    existing.InternalUpdate = true;
                    try
                    {
                        combo.ForeColor = Color.Black;
                        combo.Text = previousEffectiveText;
                    }
                    finally
                    {
                        existing.InternalUpdate = false;
                    }
                    RefreshComboItems(combo, existing, previousEffectiveText);
                    return;
                }

                existing.SelectedItem = null;
                RefreshComboItems(combo, existing, string.Empty);
                ResetToPlaceholder(combo);
                return;
            }

            var state = new ComboSearchState
            {
                AllItems = allItems ?? new List<ClientSearchItem>(),
                OnSelected = options.OnSelected,
                PlaceholderText = options.PlaceholderText ?? PlaceholderText,
                AllowEmptySelection = options.AllowEmptySelection
            };
            combo.Tag = state;

            combo.TextChanged += (s, e) => OnComboTextChanged(combo, state);
            combo.DropDown += (s, e) => OnComboDropDown(combo, state);
            combo.GotFocus += (s, e) => OnComboGotFocus(combo, state);
            combo.MouseDown += (s, e) =>
            {
                if (e is MouseEventArgs me && me.Button == MouseButtons.Left)
                    OnComboActivate(combo, state);
            };
            combo.SelectionChangeCommitted += (s, e) => OnComboSelectionCommitted(combo, state);
            combo.Leave += (s, e) => OnComboLeave(combo, state);
            combo.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                    OnComboLeave(combo, state);
            };

            ApplySearchableStyle(combo);
            RefreshComboItems(combo, state, string.Empty);
            ResetToPlaceholder(combo);
        }

        public static void ResetToPlaceholder(ComboBox combo)
        {
            var state = combo?.Tag as ComboSearchState;
            if (state == null)
                return;

            state.SelectedItem = null;
            ShowPlaceholder(combo, state);
        }

        /// <summary>程序化选中一项（用于开单页默认值 / 保留上次选择）。</summary>
        public static bool TrySelectItem(ComboBox combo, ClientSearchItem item)
        {
            var state = combo?.Tag as ComboSearchState;
            if (state == null || item == null)
                return false;

            SetSelectedItem(combo, state, item);
            return true;
        }

        public static bool TrySelectByCode(ComboBox combo, IEnumerable<ClientSearchItem> items, string code)
        {
            if (items == null || string.IsNullOrWhiteSpace(code))
                return false;

            var item = items.FirstOrDefault(i =>
                string.Equals(i.Code, code, StringComparison.OrdinalIgnoreCase));
            return TrySelectItem(combo, item);
        }

        public static bool TrySelectByName(ComboBox combo, IEnumerable<ClientSearchItem> items, string name)
        {
            if (items == null || string.IsNullOrWhiteSpace(name))
                return false;

            var item = items.FirstOrDefault(i =>
                string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase));
            return TrySelectItem(combo, item);
        }

        /// <summary>选中列表第一项（排除「无」筛选项）。</summary>
        public static bool TrySelectFirst(ComboBox combo, IEnumerable<ClientSearchItem> items)
        {
            if (items == null)
                return false;

            var item = items.FirstOrDefault(i => !IsNoneFilter(i));
            return TrySelectItem(combo, item);
        }

        public static bool TryGetSelectedClient(ComboBox combo, List<ClientSearchItem> allItems, out ClientSearchItem item)
        {
            return TryGetSelectedItem(combo, allItems, out item, allowEmptySelection: false);
        }

        public static bool TryGetSelectedItem(
            ComboBox combo,
            List<ClientSearchItem> allItems,
            out ClientSearchItem item,
            bool allowEmptySelection = false)
        {
            item = null;
            if (combo == null || allItems == null)
                return false;

            var state = combo.Tag as ComboSearchState;
            if (state?.SelectedItem != null)
            {
                item = state.SelectedItem;
                return true;
            }

            string text = GetEffectiveText(combo, state);
            if (string.IsNullOrWhiteSpace(text))
                return allowEmptySelection;

            item = allItems.FirstOrDefault(i =>
                string.Equals(i.DisplayText, text, StringComparison.OrdinalIgnoreCase));
            if (item != null)
            {
                if (state != null)
                    state.SelectedItem = item;
                return true;
            }

            string ph = state?.PlaceholderText ?? PlaceholderText;
            var matches = Filter(allItems, text, ph);
            if (matches.Count == 1)
            {
                item = matches[0];
                if (state != null)
                    state.SelectedItem = item;
                return true;
            }

            return false;
        }

        /// <summary>搜索前提交 ComboBox 输入（等同失焦校验）。</summary>
        public static void CommitComboSearchInput(ComboBox combo)
        {
            var state = combo?.Tag as ComboSearchState;
            if (state == null || combo.IsDisposed)
                return;

            OnComboLeave(combo, state);
        }

        /// <summary>解析筛选项：「无」或空白表示不限制；已选项返回 Name；否则返回用户输入文本供模糊查询。</summary>
        public static string ResolveFilterValueForQuery(ComboBox combo, List<ClientSearchItem> items)
        {
            if (combo == null)
                return string.Empty;

            CommitComboSearchInput(combo);

            if (TryGetSelectedItem(combo, items, out ClientSearchItem item, allowEmptySelection: true))
            {
                if (item == null || IsNoneFilter(item))
                    return string.Empty;
                return item.Name ?? string.Empty;
            }

            var state = combo.Tag as ComboSearchState;
            string text = GetEffectiveText(combo, state);
            if (IsNoRestrictionFilterText(text))
                return string.Empty;

            return text.Trim();
        }

        private static ClientSearchItem FindMatchingItem(List<ClientSearchItem> items, ClientSearchItem target)
        {
            if (items == null || target == null)
                return null;

            var match = items.FirstOrDefault(i =>
                string.Equals(i.Code, target.Code, StringComparison.OrdinalIgnoreCase)
                && string.Equals(i.Name, target.Name, StringComparison.OrdinalIgnoreCase));
            if (match != null)
                return match;

            if (!string.IsNullOrEmpty(target.Name))
            {
                match = items.FirstOrDefault(i =>
                    string.Equals(i.Name, target.Name, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                    return match;
            }

            if (!string.IsNullOrEmpty(target.Code))
            {
                match = items.FirstOrDefault(i =>
                    string.Equals(i.Code, target.Code, StringComparison.OrdinalIgnoreCase));
            }

            return match;
        }

        private static void OnComboGotFocus(ComboBox combo, ComboSearchState state)
        {
            ClearPlaceholderIfNeeded(combo, state);
            PrepareComboForResearch(combo, state);
            ScheduleDropDown(combo, state);
        }

        private static void OnComboActivate(ComboBox combo, ComboSearchState state)
        {
            if (state.InternalUpdate || combo.IsDisposed)
                return;

            ClearPlaceholderIfNeeded(combo, state);

            if (IsShowingSelectedDisplayText(combo, state))
                PrepareComboForResearch(combo, state);
            else
            {
                string text = GetEffectiveText(combo, state);
                RefreshComboItems(combo, state, text);
            }

            ScheduleDropDown(combo, state);
        }

        /// <summary>
        /// 已选中某项后再次聚焦/点击：清空输入框并展示全部可选项，保留 SelectedItem 供校验与失焦恢复。
        /// </summary>
        private static void PrepareComboForResearch(ComboBox combo, ComboSearchState state)
        {
            if (state.SelectedItem == null)
                return;

            string text = GetEffectiveText(combo, state);
            if (string.IsNullOrWhiteSpace(text))
            {
                RefreshComboItems(combo, state, string.Empty);
                return;
            }

            if (!string.Equals(text, state.SelectedItem.DisplayText, StringComparison.OrdinalIgnoreCase))
                return;

            state.InternalUpdate = true;
            try
            {
                combo.ForeColor = Color.Black;
                combo.Text = string.Empty;
            }
            finally
            {
                state.InternalUpdate = false;
            }

            RefreshComboItems(combo, state, string.Empty);
        }

        private static bool IsShowingSelectedDisplayText(ComboBox combo, ComboSearchState state)
        {
            return state.SelectedItem != null
                && string.Equals(
                    GetEffectiveText(combo, state),
                    state.SelectedItem.DisplayText,
                    StringComparison.OrdinalIgnoreCase);
        }

        private static void ClearPlaceholderIfNeeded(ComboBox combo, ComboSearchState state)
        {
            if (GetEffectiveText(combo, state).Length != 0 || combo.Text != state.PlaceholderText)
                return;

            state.InternalUpdate = true;
            try
            {
                combo.ForeColor = Color.Black;
                combo.Text = string.Empty;
            }
            finally
            {
                state.InternalUpdate = false;
            }
        }

        private static void OnComboTextChanged(ComboBox combo, ComboSearchState state)
        {
            if (state.InternalUpdate)
                return;

            if (combo.SelectedItem is ClientSearchItem selected)
            {
                string currentText = GetEffectiveText(combo, state);
                if (string.Equals(currentText, selected.DisplayText, StringComparison.OrdinalIgnoreCase))
                {
                    if (state.SelectedItem?.Code != selected.Code || state.SelectedItem?.Name != selected.Name)
                    {
                        state.SelectedItem = selected;
                        state.OnSelected?.Invoke(selected);
                    }
                    return;
                }

                // 用户正在输入以搜索其他项，清除 WinForms 选中状态
                state.InternalUpdate = true;
                try
                {
                    combo.SelectedIndex = -1;
                    combo.SelectedItem = null;
                }
                finally
                {
                    state.InternalUpdate = false;
                }
            }

            string text = GetEffectiveText(combo, state);
            state.SelectedItem = null;
            RefreshComboItems(combo, state, text);

            if (!combo.DroppedDown)
                ScheduleDropDown(combo, state);
            else
                RestoreComboCaret(combo, state);
        }

        private static void ScheduleDropDown(ComboBox combo, ComboSearchState state)
        {
            combo.BeginInvoke(new Action(() => OpenDropDownIfNeeded(combo, state)));
        }

        private static void OpenDropDownIfNeeded(ComboBox combo, ComboSearchState state)
        {
            if (combo.IsDisposed || state.InternalUpdate)
                return;

            if (combo.DroppedDown)
            {
                RestoreComboCaret(combo, state);
                return;
            }

            string text = GetEffectiveText(combo, state);
            if (!string.Equals(text, state.LastFilterKeyword, StringComparison.Ordinal))
                RefreshComboItems(combo, state, text);

            if (combo.Items.Count == 0)
                return;

            combo.DroppedDown = true;
            Cursor.Current = Cursors.Default;
            combo.Cursor = Cursors.Default;
            Cursor.Show();
            RestoreComboCaret(combo, state);
        }

        private static void OnComboDropDown(ComboBox combo, ComboSearchState state)
        {
            if (state.InternalUpdate)
                return;

            string text = GetEffectiveText(combo, state);

            if (state.SelectedItem != null && string.IsNullOrWhiteSpace(text))
            {
                RefreshComboItems(combo, state, string.Empty);
                return;
            }

            if (IsShowingSelectedDisplayText(combo, state))
            {
                PrepareComboForResearch(combo, state);
                return;
            }

            if (string.Equals(text, state.LastFilterKeyword, StringComparison.Ordinal))
                return;

            RefreshComboItems(combo, state, text);
        }

        private static void OnComboSelectionCommitted(ComboBox combo, ComboSearchState state)
        {
            if (combo.SelectedItem is ClientSearchItem selected)
            {
                state.SelectedItem = selected;
                state.OnSelected?.Invoke(selected);
            }
        }

        private static void OnComboLeave(ComboBox combo, ComboSearchState state)
        {
            if (state.InternalUpdate)
                return;

            string text = GetEffectiveText(combo, state);
            if (string.IsNullOrWhiteSpace(text))
            {
                if (state.SelectedItem != null)
                {
                    SetSelectedItem(combo, state, state.SelectedItem);
                    return;
                }

                ShowPlaceholder(combo, state);
                return;
            }

            var exact = state.AllItems.FirstOrDefault(i =>
                string.Equals(i.DisplayText, text, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
            {
                SetSelectedItem(combo, state, exact);
                return;
            }

            var matches = Filter(state.AllItems, text, state.PlaceholderText);
            if (matches.Count == 1)
            {
                SetSelectedItem(combo, state, matches[0]);
                return;
            }

            state.SelectedItem = null;
        }

        private static void RefreshComboItems(ComboBox combo, ComboSearchState state, string keyword)
        {
            string currentText = combo.Text ?? string.Empty;
            if (currentText == state.PlaceholderText || currentText == PlaceholderText || currentText == NamePlaceholderText)
                currentText = string.Empty;

            var filtered = Filter(state.AllItems, keyword, state.PlaceholderText);

            state.InternalUpdate = true;
            try
            {
                combo.BeginUpdate();
                combo.Items.Clear();
                foreach (var item in filtered)
                    combo.Items.Add(item);

                combo.SelectedIndex = -1;
                combo.SelectedItem = null;

                if (!string.Equals(combo.Text, currentText, StringComparison.Ordinal))
                    combo.Text = currentText;
            }
            finally
            {
                combo.EndUpdate();
                state.InternalUpdate = false;
            }

            state.LastFilterKeyword = keyword ?? string.Empty;
            RestoreComboCaret(combo, state);
        }

        private static void RestoreComboCaret(ComboBox combo, ComboSearchState state)
        {
            string text = combo.Text ?? string.Empty;
            if (text == state?.PlaceholderText || text == PlaceholderText || text == NamePlaceholderText)
                text = string.Empty;

            int pos = text.Length;
            if (pos >= 0 && combo.SelectionStart != pos)
            {
                combo.SelectionStart = pos;
                combo.SelectionLength = 0;
            }
        }

        private static void SetSelectedItem(ComboBox combo, ComboSearchState state, ClientSearchItem item)
        {
            state.InternalUpdate = true;
            try
            {
                combo.ForeColor = Color.Black;
                combo.Text = item.DisplayText;
                RestoreComboCaret(combo, state);
            }
            finally
            {
                state.InternalUpdate = false;
            }

            state.SelectedItem = item;
            state.OnSelected?.Invoke(item);
        }

        private static void ShowPlaceholder(ComboBox combo, ComboSearchState state)
        {
            state.InternalUpdate = true;
            try
            {
                combo.ForeColor = Color.Gray;
                combo.Text = state.PlaceholderText;
            }
            finally
            {
                state.InternalUpdate = false;
            }
        }

        private static string GetEffectiveText(ComboBox combo, ComboSearchState state)
        {
            string text = combo.Text ?? string.Empty;
            if (state != null && text == state.PlaceholderText)
                return string.Empty;
            if (text == PlaceholderText || text == NamePlaceholderText)
                return string.Empty;
            return text.Trim();
        }

        private sealed class ComboSearchState
        {
            public List<ClientSearchItem> AllItems { get; set; }
            public ClientSearchItem SelectedItem { get; set; }
            public bool InternalUpdate { get; set; }
            public string LastFilterKeyword { get; set; } = string.Empty;
            public string PlaceholderText { get; set; } = ClientSearchHelper.PlaceholderText;
            public bool AllowEmptySelection { get; set; }
            public Action<ClientSearchItem> OnSelected { get; set; }
        }
    }
}
