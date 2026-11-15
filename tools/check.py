"""Static checks for the plugin project (no .NET compiler needed).

Usage: python3 -I check.py <project_dir> gen_summary.json
Needs: pip install tree-sitter tree-sitter-c-sharp
Writes ui_values.json (every value the settings page can set) for test_ui_values.js.
"""
import glob
import json
import os
import re
import sys
import xml.etree.ElementTree as ET

import tree_sitter_c_sharp as tscs
from tree_sitter import Language, Parser

proj = sys.argv[1]
summary = json.load(open(sys.argv[2]))
here = os.path.dirname(os.path.abspath(sys.argv[2]))
ok = True


def fail(msg):
    global ok
    ok = False
    print('FAIL:', msg)


def read(*parts):
    return open(os.path.join(proj, *parts), encoding='utf-8-sig').read()


# 1. C# syntax (tree-sitter) and language level (project has no LangVersion: C# 7.3 on net48)
parser = Parser(Language(tscs.language()))
cs_files = sorted(glob.glob(os.path.join(proj, '**', '*.cs'), recursive=True))
all_cs = ''
for f in cs_files:
    src = open(f, 'rb').read()
    tree = parser.parse(src)
    errors = []

    def walk(n):
        if n.type == 'ERROR' or n.is_missing:
            errors.append((n.start_point, n.type))
        for c in n.children:
            walk(c)

    walk(tree.root_node)
    print(('ok   ' if not errors else 'FAIL ') + 'syntax', os.path.relpath(f, proj), '' if not errors else errors[:3])
    if errors:
        ok = False
    text = re.sub(r'//.*', '', src.decode('utf-8-sig'))
    all_cs += text
    for pat, what in [(r'\?\?=', '??= (C# 8)'), (r'\bis not\b', 'is not (C# 9)'),
                      (r'\busing\s+(var|[A-Z]\w*)\s+\w+\s*=[^;]*;\s*$', 'using declaration (C# 8)'),
                      (r'\bnew\(\)', 'target-typed new (C# 9)'), (r'\binit;', 'init accessor (C# 9)'),
                      (r'\brecord\b', 'record (C# 9)'), (r'\bswitch\s*\{', 'switch expression (C# 8)')]:
        if re.search(pat, text, re.M):
            fail(f'{os.path.basename(f)} uses {what}')

# 2. Settings.cs (generated) vs script defaults
settings = read('Settings.cs')
cs_props = re.findall(r'public (bool|int|string) (\w+)\s*\n\s*\{', settings)
cs_defaults = {}
for t, name in cs_props:
    m = re.search(r'private %s _%s = (.+);' % (t, name[0].lower() + name[1:]), settings)
    v = m.group(1)
    cs_defaults[name] = (v == 'true') if t == 'bool' and v in ('true', 'false') else (int(v) if t == 'int' else v.strip('"'))
js_defaults = summary['defaults']
if cs_defaults != js_defaults:
    fail('Settings.cs defaults differ from SL_DEFAULTS: %s' % {k for k in set(cs_defaults) | set(js_defaults) if cs_defaults.get(k) != js_defaults.get(k)})
else:
    print('ok   Settings.cs matches SL_DEFAULTS (%d settings, names/types/defaults)' % len(cs_defaults))
reset = set(re.findall(r'^\s{12}(\w+) = ', settings.split('ResetToDefaults')[1], re.M))
if reset != set(cs_defaults):
    fail('ResetToDefaults does not cover all settings')
else:
    print('ok   ResetToDefaults covers all settings')

# 2b. "Reset this tab": ResetSetting covers every setting; every reset button names a tab that TabSettings knows
rs_cases = set(re.findall(r'case "(\w+)":', settings.split('public bool ResetSetting')[1]))
if rs_cases != set(cs_defaults):
    fail('ResetSetting does not cover all settings')
tab_cs = open(os.path.join(proj, 'TabSettings.cs'), encoding='utf-8-sig').read()
tab_names = dict((m.group(1), re.findall(r'"(\w+)"', m.group(2))) for m in re.finditer(r'\{ "([^"]+)", new\[\] \{([^}]*)\} \}', tab_cs))
led_xaml = open(os.path.join(proj, 'LedPage.xaml'), encoding='utf-8-sig').read()
tags = re.findall(r'Content="Reset this tab" Tag="([^"]+)"', led_xaml)
heads = re.findall(r'<TabItem Header="([^"]+)"', led_xaml)
for t in tags:
    if t not in tab_names:
        fail('Reset this tab: unknown tab %s' % t)
for h in heads:
    if h not in tags:
        fail('tab %s has no "Reset this tab" button' % h)
for t, names in tab_names.items():
    for n in names:
        if n not in cs_defaults:
            fail('TabSettings: %s is not a setting (tab %s)' % (n, t))
if ok:
    print('ok   Reset this tab: %d tabs, buttons and settings match' % len(tags))

# 3. XAML files (pages, controls, styles)
xaml_files = sorted(glob.glob(os.path.join(proj, '*.xaml')))
texts = {}
for path in xaml_files:
    name = os.path.basename(path)
    texts[name] = read(name)
    try:
        ET.fromstring(texts[name].encode('utf-8'))
    except ET.ParseError as e:
        fail(f'{name} parse error: {e}')
print('ok   %d XAML files are well-formed' % len(texts))

class_names = set(re.findall(r'\bclass (\w+)', all_cs))
declared_names = {}
for name, xt in texts.items():
    m = re.search(r'x:Class="([\w.]+)"', xt)
    declared_names[name] = set(re.findall(r'x:Name="(\w+)"', xt))
    if not m:
        continue
    code_name = name + '.cs'
    if not os.path.exists(os.path.join(proj, code_name)):
        fail(f'{name} has x:Class but no {code_name}')
        continue
    code = read(code_name)
    ns_m, cl_m = re.search(r'namespace ([\w.]+)', code), re.search(r'partial class (\w+)', code)
    if m.group(1) != f'{ns_m.group(1)}.{cl_m.group(1)}':
        fail(f'{name}: x:Class {m.group(1)} != code-behind {ns_m.group(1)}.{cl_m.group(1)}')
    for handler in set(re.findall(r'(?<![A-Za-z])(?:Click|ValueChanged|Checked|TextChanged|SelectionChanged)="(\w+)"', xt)):
        if f'void {handler}(' not in code:
            fail(f'{name}: missing handler {handler}')
    if 'public ' + cl_m.group(1) + '()' not in code:
        fail(f'{name}: code-behind has no parameterless constructor')
    # elements the code talks to must exist in the XAML
    skip = class_names | {'System', 'Data', 'Plugin', 'Index', 'Settings', 'Clipboard', 'Process', 'Directory', 'Environment', 'Visibility'}
    for ident in set(re.findall(r'\b([A-Z]\w+)\.(?:ItemsSource|SelectedItem|SelectedIndex|Items|Visibility|Children|Value|Text|ScrollIntoView|Content|IsChecked)\b', code)):
        if ident not in skip and ident not in declared_names[name]:
            fail(f'{name}: code uses {ident} but the XAML has no x:Name="{ident}"')
print('ok   code-behind classes, handlers and named elements match')

all_xaml = '\n'.join(texts.values())
pages_xaml = '\n'.join(t for n, t in texts.items() if n != 'ShiftlineStyles.xaml')
hand = read('Settings.Themes.cs') + read('Settings.App.cs')
plugin_props = set(re.findall(r'public (?:bool|int|string|ObservableCollection<\w+>) (\w+)\s*\n?\s*\{', hand))
allowed_extra = plugin_props | {'Car', 'CarOptions', 'IsDropDownOpen', 'Favourite', 'Badge', 'Game', 'Name', 'Hex', 'Stops', 'SelectedItem', 'IsChecked', 'Foreground', 'IsEditable', 'IsBuiltIn'}
bindings = set(re.findall(r'\{Binding (\w+)[,}]', all_xaml))
unknown = bindings - set(cs_defaults) - allowed_extra
if unknown:
    fail(f'bindings without a matching property: {unknown}')
else:
    print('ok   all %d bound names exist' % len(bindings))
unbound = set(cs_defaults) - bindings - {'LayerOrder'}      # LayerOrder is edited by the priority list in code
if unbound:
    fail(f'script settings without a control on any page: {unbound}')
else:
    print('ok   every script setting has a control')
unbound_app = (plugin_props - {'Themes', 'ThemesSeeded', 'ActiveProfile', 'Id', 'Rank', 'Label', 'Display'}) - bindings      # the last four belong to the priority list items
if unbound_app:
    fail(f'plugin settings without a control: {unbound_app}')

keys = set()
for t in texts.values():
    keys |= set(re.findall(r'x:Key="(\w+)"', t))
for name, t in texts.items():
    for key in set(re.findall(r'(?:StaticResource|DynamicResource) (\w+)', t)):
        if key not in keys:
            fail(f'{name}: missing resource {key}')
    for typ in set(re.findall(r'local:(\w+)', t)):
        if typ not in class_names:
            fail(f'{name}: unknown local type {typ}')
    if name != 'ShiftlineStyles.xaml' and 'StaticResource' in t and '/Shiftline.Plugin;component/ShiftlineStyles.xaml' not in t:
        fail(f'{name} does not merge ShiftlineStyles.xaml (StaticResource lookups would fail)')
print('ok   resources, local types and merged styles resolve')

# slider limits must match the script's limits
for prop, limit in (('SideLeds', summary['maxSide']), ('CentreLeds', summary['maxCentre'])):
    m = re.search(r'Maximum="(\d+)" Value="\{Binding %s,' % prop, pages_xaml)
    if not m or int(m.group(1)) != limit:
        fail(f'{prop} slider maximum {m.group(1) if m else None} != script limit {limit}')
    else:
        print(f'ok   {prop} slider maximum = script limit {limit}')

# values the page can set: ComboBox items and RadioButton tiles
ui = {}
for name, t in texts.items():
    if name == 'ShiftlineStyles.xaml':
        continue
    for sel in ET.fromstring(t.encode('utf-8')).iter('{http://schemas.microsoft.com/winfx/2006/xaml/presentation}ComboBox'):
        sv = sel.get('SelectedValue')
        if sv:
            ui.setdefault(re.match(r'\{Binding (\w+)', sv).group(1), []).extend(i.get('Tag') for i in sel)
for prop, value in re.findall(r'IsChecked="\{Binding (\w+), Converter=\{StaticResource StrEq\}, ConverterParameter=([^}]+)\}"', pages_xaml):
    ui.setdefault(prop, []).append(value)
json.dump(ui, open(os.path.join(here, 'ui_values.json'), 'w'), indent=1)
for prop, vals in ui.items():
    if prop not in js_defaults:
        continue                      # a choice that belongs to the plugin (for example the game of a preset), not to the LED script
    if js_defaults[prop] not in vals:
        fail(f'default of {prop} ({js_defaults[prop]}) is not selectable on the page')
print('ok   every default is selectable (%d choice settings)' % len(ui))

# the function tabs: every tab that depends on the LED function names a known area
for area in re.findall(r'FnTab\}, ConverterParameter=(\w+)', pages_xaml):
    if area not in ('rpm', 'brake', 'pedals', 'fuel'):
        fail('unknown function tab area ' + area)

# 4. csproj
cp_text = read('Shiftline.csproj')
try:
    ET.fromstring(cp_text.encode('utf-8'))
    print('ok   csproj is well-formed')
except ET.ParseError as e:
    fail('csproj: %s' % e)
for inc in re.findall(r'<(?:Compile|Page) Include="([^"]+)"', cp_text):
    if not os.path.exists(os.path.join(proj, inc.replace('\\', os.sep))):
        fail('csproj includes missing file ' + inc)
included = {i.replace('\\', '/') for i in re.findall(r'<(?:Compile|Page) Include="([^"]+)"', cp_text)}
for f in cs_files + xaml_files:
    rel = os.path.relpath(f, proj).replace(os.sep, '/')
    if rel not in included:
        fail('file not in csproj: ' + rel)
print('ok   csproj lists all source files')
for inc in re.findall(r'<EmbeddedResource Include="([^"]+)"', cp_text):
    if not os.path.exists(os.path.normpath(os.path.join(proj, inc.replace('\\', os.sep)))):
        fail('csproj embeds missing file ' + inc)
print('ok   embedded resources exist')

# 5. plugin class contract (names the SDK demo uses) and the colour lists the script reads
pl = read('Shiftline.cs')
for needle in ['IPlugin, IDataPlugin, IWPFSettingsV2', 'public PluginManager PluginManager { get; set; }', 'public ImageSource PictureIcon',
               'public string LeftMenuTitle', 'public void DataUpdate(PluginManager pluginManager, ref GameData data)',
               'public void End(PluginManager pluginManager)', 'public System.Windows.Controls.Control GetWPFSettingsControl(PluginManager pluginManager)',
               'public void Init(PluginManager pluginManager)', 'ReadCommonSettings<Settings>', 'SaveCommonSettings', 'AttachDelegate',
               'Settings.ScriptSettingNames']:
    if needle not in pl:
        fail('plugin missing: ' + needle)
script_lists = set(re.findall(r"sl_colourList\('(\w+)'\)", open(os.path.join(here, 'Shiftline.plugin.js'), encoding='utf-8').read()))
for name in script_lists:
    if f'AttachDelegate("{name}"' not in pl:
        fail(f'the script reads {name} but the plugin does not publish it')
print('ok   plugin implements the SDK demo contract and publishes %s' % ', '.join(sorted(script_lists)))
sys.exit(0 if ok else 1)
