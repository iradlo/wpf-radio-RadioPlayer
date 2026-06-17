import sys, json, subprocess

d = json.load(sys.stdin)
f = d.get("tool_input", {}).get("file_path", "")

if any(f.endswith(e) for e in (".cs", ".xaml", ".csproj")):
    r = subprocess.run(
        ["dotnet", "build", "D:/Code/.NET/wpf-radio/RadioPlayer", "--no-restore", "-v", "quiet"],
    )
    sys.exit(r.returncode)
