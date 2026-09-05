using System;

namespace lengkubao.desktop
{
    /// <summary>
    /// 客户搜索下拉项：编号、名称、显示文本与拼音首字母。
    /// </summary>
    public class ClientSearchItem
    {
        public string Code { get; set; }
        public string Name { get; set; }
        public string DisplayText { get; set; }
        public string Initials { get; set; }

        public override string ToString()
        {
            return DisplayText ?? string.Empty;
        }
    }
}
