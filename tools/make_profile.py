"""Builds the importable SimHub LED profile (.ledsprofile) from Shiftline.plugin.js.

The file format was read off a profile exported by SimHub: a JSON tree of containers. This
profile uses a single ScriptedContent node that calls sl_frame() for all LEDs (SL_MAX_TOTAL entries, the layout is chosen in the plugin).

Usage: python3 -I make_profile.py <script.js> <out.ledsprofile> <profile name> [default TestMode]
       python3 -I make_profile.py --loader <loader.js> <out.ledsprofile> <profile name>      (profile that loads the script from the plugin)
"""
import hashlib
import re
import json, sys, uuid

STRUCT = {'GlobalBrightnessPreset': {'CurrentMode': 0, 'Brightness': 100.0, 'BrightnessSettings': {}},
 'TestLedsGameData': {'CarModel': None,
                      'CarId': None,
                      'CarChoice': {'CarId': None, 'CarModel': None, 'GameCode': None, 'IsSet': False},
                      'GearEx': 0,
                      'FuelMax': 100.0,
                      'RPMSMax': 10000.0,
                      'TurboMax': 3.5,
                      'TurnIndicatorLeftEnabled': False,
                      'TurnIndicatorRightEnabled': False,
                      'SessionBestDeltaEx': 0.0,
                      'AllTimeBestDeltaEx': 0.0,
                      'DRSEnabled': False,
                      'DRSAvailable': False,
                      'MaxRpm': 10000.0,
                      'GameRunning': False,
                      'Brake': 0.0,
                      'GameName': 'IRacing',
                      'Fuel': 0.0,
                      'RPMPercent': 0.0,
                      'RPMRedlineReached': False,
                      'SpeedKmh': 0.0,
                      'SpeedMph': 0.0,
                      'Turbo': 0.0,
                      'TurboPercent': 0.0,
                      'Gear': 'N',
                      'IsInPitLane': False,
                      'PitLimiterOn': False,
                      'AbsEnabled': False,
                      'AbsActive': False,
                      'TCEnabled': False,
                      'TCActive': False,
                      'LowFuelAlert': False,
                      'SpotterCarLeft': False,
                      'SpotterCarRight': False,
                      'BlackFlag': False,
                      'YellowFlag': False,
                      'WhiteFlag': False,
                      'BlueFlag': False,
                      'GreenFlag': False,
                      'CarStartedTime': '0001-01-01T00:00:00',
                      'SessionBestDelta': 0.0,
                      'AllTimeBestDelta': 0.0}}

def build(js_path, out_path, name, test_mode=None, loader=False):
    js = open(js_path, encoding='utf-8').read()
    build = hashlib.sha1(js.replace('\r\n', '\n').encode('utf-8')).hexdigest()[:8]     # the same number the plugin shows on its Home page
    name = '%s [%s]' % (name, build)
    if loader:
        max_total = 48          # 2 * SL_MAX_SIDE + SL_MAX_CENTRE of the script
    else:
        max_total = (2 * int(re.search(r'const SL_MAX_SIDE = (\d+);', js).group(1)) +
                     int(re.search(r'const SL_MAX_CENTRE = (\d+);', js).group(1)))
    if test_mode:
        assert "TestMode: 'Off'," in js
        js = js.replace("TestMode: 'Off',", "TestMode: '%s'," % test_mode, 1)
    node = {
        'ContentFormula': {'JSExt': 4, 'Interpreter': 1, 'Expression': 'return sl_loader();' if loader else 'return sl_frame();'},
        'LedCount': max_total,
        'ContainerType': 'ScriptedContent',
        'Description': 'Shiftline (composite: rev, brake, pit, flags, status, idle)',
    }
    group = {'LedContainers': [node], 'ContainerType': 'Base.Group', 'Description': 'Shiftline'}
    profile = {
        'CarChoices': [],
        'UseStrictJSIsolation': False,
        'EmbeddedJavascript': js.replace('\r\n', '\n').replace('\n', '\r\n'),
        'GlobalBrightness': 100.0,
        'GlobalBrightnessPreset': STRUCT['GlobalBrightnessPreset'],
        'LedContainers': [group],
        'TestLedsGameData': STRUCT['TestLedsGameData'],
        'UseProfileBrightness': False,
        'LastLoaded': '0001-01-01T00:00:00',
        'Description': 'Own LED profile (layout set in the plugin): RPM bar, brake bar, spotter, flags, pit lane, status LEDs, idle animation. '
                       'Settings come from the Shiftline plugin (defaults apply without it).',
        'Name': name,
        'ProfileId': str(uuid.uuid4()),
        'GameCode': None,
        'CarChoice': None,
    }
    text = json.dumps(profile, indent=2, ensure_ascii=False).replace('\n', '\r\n')
    with open(out_path, 'w', encoding='utf-8', newline='') as f:
        f.write(text)

if __name__ == '__main__':
    if sys.argv[1] == '--loader':
        build(sys.argv[2], sys.argv[3], sys.argv[4], None, True)
        print('written', sys.argv[3])
    else:
        build(sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4] if len(sys.argv) > 4 else None)
        print('written', sys.argv[2])
