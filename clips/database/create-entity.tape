Source clips/_settings.tape

Output "clips-output/cli/database/create-entity.mp4"
Output "clips-output/cli/database/create-entity.webm"

Hide
Type 'clear'
Enter
Type 'export PATH=/opt/homebrew/bin:/opt/homebrew/opt/dotnet@8/bin:$PATH'
Enter
Type 'export DOTNET_ROOT=/opt/homebrew/opt/dotnet@8/libexec'
Enter
Type 'anythink config use content_builder'
Enter
Type "export PS1='$ '"
Enter
Type 'clear'
Enter
Sleep 6s
Show

Type@20ms 'anythink entities create orders'
Sleep 300ms
Enter
Sleep 2.8s

Type@20ms 'anythink fields add orders total --type decimal --required'
Sleep 300ms
Enter
Sleep 2.8s

Type@20ms 'anythink fields add orders status --type varchar --display select --options pending,paid'
Sleep 300ms
Enter
Sleep 3s

Type@20ms 'anythink data list orders'
Sleep 300ms
Enter
Sleep 2.8s

Hide
Type 'anythink entities delete orders --yes'
Enter
Sleep 4s
Type 'anythink config use content_builder'
Enter
Sleep 2s
