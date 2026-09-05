path = "Form1.cs"
with open(path, "r", encoding="utf-8") as f:
    s = f.read()
if "using System.Linq;" not in s:
    s = s.replace("using System.Data;\n", "using System.Data;\nusing System.Linq;\n")
