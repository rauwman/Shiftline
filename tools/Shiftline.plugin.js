// Shiftline.js v3 - own LED logic for a LED dashboard with configurable layout (default 5 | 16 | 5)
// (SimHub LED profile, embedded JavaScript)
//
// Entry point: sl_frame() returns one colour (#AARRGGBB) or null per LED. The layout comes from the plugin
// settings SideLeds / CentreLeds; the array is padded with null (transparent) up to SL_MAX_TOTAL entries.
// LED order: left side (index 0 = outermost) | centre | right side (last = outermost).
// null means "transparent"; '#FF000000' is opaque black (switches the LED off).
//
// All layers are painted from low to high priority inside sl_frame(), so the result
// does not depend on how SimHub orders layers.
//
// Side LED slots (0 = outermost, SL_SIDE-1 = innermost), mirrored on both sides unless noted. For the default
// 5 LEDs per side the roles sit on these slots (see sl_slots(); with fewer LEDs some roles share a slot):
//   0  ABS / headlight flasher        3  ERS state of charge (right), ERS mode area (left, unused)
//   1  Traction control               4  turn indicator (own side, always the innermost LED)
//   2  DRS (both) / KERS lap limit (left)
// Sides are overridden by: brake bar (all side LEDs; ABS and TC stay on top), low fuel chase, spotter, flags, side pit warning.

// Largest supported layout. The profile's scripted content is created with SL_MAX_TOTAL LEDs;
// the actual layout is chosen in the plugin (settings SideLeds and CentreLeds).
const SL_MAX_SIDE = 8;
const SL_MAX_CENTRE = 32;
const SL_MAX_TOTAL = SL_MAX_SIDE * 2 + SL_MAX_CENTRE;
let SL_SIDE = 5;                                  // LEDs per side (set by sl_loadConfig)
let SL_CENTRE = 16;                               // centre LEDs (set by sl_loadConfig)
let SL_TOTAL = SL_SIDE * 2 + SL_CENTRE;
const SL_OFF = '#FF000000';

// ============================== configuration ==================================
// Every value in SL_DEFAULTS can be changed on the "Shiftline" page of the SimHub plugin,
// which publishes it as the property Shiftline.<Name>. Without the plugin the defaults apply.
// Colours are #RRGGBB here and are converted to #AARRGGBB when loaded.

const SL_DEFAULTS = {
	// Layout
	SideLeds: 5,                   // LEDs on each side, 1 to SL_MAX_SIDE (outer groups)
	CentreLeds: 16,                // LEDs in the centre, 2 to SL_MAX_CENTRE
	// What the LEDs do (chosen in the plugin): rpmBrake | rpmOnly | brakeOnly | brakeThrottle | fuel | flags
	//   rpmBrake      rev bar in the centre + brake bar          rpmOnly   rev bar only
	//   brakeOnly     brake bar only                             brakeThrottle  throttle and brake pedals in the centre
	//   fuel          fuel gauge in the centre                   flags     flags across the whole strip
	LedFunction: 'rpmBrake',
	// Brightness (percent). General applies to everything, Idle only to the idle animation on top of it.
	BrightnessGeneral: 100,
	NightMode: false,              // night mode: halves the brightness of everything (general and idle), switchable with a hotkey
	BrightnessIdle: 40,
	// RPM bar
	RpmStyle: 'middle',            // middle (outside to centre) | leftRight | simhub (left to right on SimHub's per-car shift points) | centreOut | rightLeft | f1
	RpmThemeId: 'shiftline',       // colour theme of the rev bar (chosen in the plugin; the plugin publishes RpmColours)
	RpmColourMode: 'blocks',       // blocks | gradient
	RpmStartPercent: 70,           // first LED lights at this % of redline
	RpmEndPercent: 97,             // last LED lights at this % of redline
	RpmRedline: 'flash',           // at redline: flash (one colour) | flashAlt (two colours on every second LED) | flashSwap (the whole bar in colour 1, then in colour 2) | off
	RpmShiftFlash: false,          // the redline behaviour also happens at SimHub's shift point of the car and gear (Car settings in SimHub), not only at the redline
	RpmNoFlashInTopGear: true,     // no redline behaviour in the highest gear (there is nothing to shift into)
	RpmFlashColour: '#FF0000',
	RpmFlashColour2: '#0000FF',    // second colour of flashAlt
	// Brake bar
	BrakeArea: 'both',             // where the brake bar is shown: both (sides) | left | right | top (needs the function Brake)
	BrakeDirection: 'outIn',       // how it fills: outIn | leftRight | rightLeft | inOut (left/right only on the top row)
	FuelSmooth: false,             // the last LED of the fuel level shows only its share
	BrakeSmooth: false,            // the next LED fades in instead of switching on at once
	PedalsSmooth: false,           // the same for the pedal bars
	BrakeColour: '#FF0000',
	ThrottleColour: '#00FF00',
	// Pedal display (function brakeThrottle): where every pedal is shown. Areas: off | centreMiddle (from the middle outwards) | centreLeft (left to
	// right) | centreRight (right to left) | halfLeft (from the middle to the left) | halfRight (from the middle to the right) | centreEnds (from both
	// ends inwards) | sideLeft | sideRight | sideBoth (side LEDs, from the outermost LED inwards). The brake is painted over the throttle over the clutch.
	PedalBrake: 'halfLeft',
	PedalThrottle: 'halfRight',
	PedalClutch: 'off',
	BrakeBlinkStage1: 80,          // % brake: upper LEDs start blinking
	BrakeBlinkStage2: 100,         // % brake: blinking gets faster
	// Idle animation (no game running)
	IdleEnabled: true,
	IdleStyle: 'sweep',            // sweep | wave | comet | fillDrain | centreOut | solid | colourFade | breathe |
	                               // bounce | orbit | theatreChase | alternate | wipe | rain | ripple | twinkle
	IdleSpeed: 'normal',           // slow | normal | fast
	LayerOrder: '',                // priority order of the effects, lowest first, comma separated ids (see SL_LAYER_DEFS); empty = standard order
	FuelPlacement: 'off',          // off | centre | sides | left | right: where the fuel level shows while nothing else uses that area (works with every LED function)
	FuelThemeId: 'fuel',           // colour theme of the fuel gauge (the plugin publishes FuelColours)
	FuelColourMode: 'gradient',    // blocks | gradient
	IdleThemeId: 'shiftline',        // colour theme of the idle animation (the plugin publishes IdleColours)
	// Options
	ShowClutch: false,             // clutch bar in the centre
	ShowErsRegen: false,           // ERS regen marker
	SidePit: true,                 // pit speeding / limiter warning on the side LEDs
	// Effects: every effect has a switch (FxXxxOn) and the colours it uses. Defaults are the colours the games use.
	FxMasterOn: true,                     // main switch of all effects (flags, spotter, pit lane, car systems, ERS, extras); the RPM and brake bars stay
	// Group switches: every group of the Effects page can be switched off as a whole; the switches of its effects stay as they are.
	FxGroupIndicatorsOn: true,            // marshal flags, spotter
	FxGroupPitOn: true,                   // pit lane indicator, alerts, speeding, limiter, pit warnings on the side LEDs
	FxGroupCarOn: true,                   // engine start, low fuel, TC, ABS, headlight flash, turn indicators, lift and coast
	FxGroupErsOn: true,                   // ERS mode, deploy, state of charge, regeneration, lap limit, push to pass
	FxGroupExtrasOn: true,                // clutch bar, DRS, DRS detection point, safety car delta
	FxFlagsOn: true,
	FxFlagRedColour: '#FF0000',
	FxFlagBlackColour: '#808080',
	FxFlagYellowColour: '#FFFF00',        // yellow flag and safety car
	FxFlagWhiteColour: '#FFFFFF',
	FxFlagOrangeColour: '#FF8C00',
	FxFlagBlueColour: '#0000FF',
	FxFlagGreenColour: '#00FF00',
	FxFlagCheckeredColour: '#FFFFFF',
	FxSpotterOn: true,
	FxSpotterColour: '#FF1493',
	FxPitIndicatorOn: true,
	FxPitIndicatorColour: '#FFFF00',
	FxPitAlertsOn: true,                  // entering the pit lane without limiter / leaving it with the limiter on
	FxPitEntryColour: '#FF0000',
	FxPitExitColour: '#00FF00',
	FxPitSpeedingOn: true,
	FxPitSpeedingColour: '#FF0000',
	FxPitLimiterOn: true,
	FxPitLimiterColour1: '#0000FF',
	FxPitLimiterColour2: '#FFFFFF',
	FxEngineStartOn: true,
	FxEngineStartColour: '#FF4500',
	FxLowFuelOn: true,
	FxLowFuelColour: '#FF0000',
	FxTcOn: true,
	FxTcColour: '#00BFFF',
	FxAbsOn: true,
	FxAbsColour: '#FFFF00',
	FxHeadlightFlashOn: true,
	FxHeadlightFlashColour: '#00BFFF',
	FxTurnOn: true,
	FxTurnColour: '#FFA500',
	FxLiftCoastOn: true,
	FxLiftCoastColour: '#BF00FF',
	FxErsDeployOn: true,
	FxErsDeployColour: '#0000FF',
	FxErsChargeOn: true,                  // state of charge: colours change with the level
	FxErsRegenColour: '#FF1493',          // switched on with ShowErsRegen
	FxErsLapLimitOn: true,                // energy left for this lap: colours change with the level
	FxP2pOn: true,
	FxP2pColour: '#008000',
	FxDrsOn: true,
	FxDrsColour: '#008000',
	FxDrsDetectOn: true,
	FxDrsDetectColour: '#FFFF00',
	FxClutchColour: '#8A2BE2',            // switched on with ShowClutch
	FxErsModeOn: true,                    // F1 games: the ERS deployment mode of the car (medium, hotlap, overtake)
	FxErsModeMediumColour: '#00C060',
	FxErsModeHotlapColour: '#FF8C00',
	FxErsModeOvertakeColour: '#FF0000',
	FxScDeltaOn: true,                    // F1 games: under the safety car, how far you are from the delta time
	FxScDeltaOkColour: '#00FF00',
	FxScDeltaOffColour: '#FFA500',
	FxScDeltaSwap: false,                 // swaps the direction of the delta display
	// Custom positions of the side effects: FxXxxCustom switches the own position on, FxXxxStart (1 = outermost LED) and FxXxxCount set it.
	// While the custom position is off the plugin keeps Start and Count at the standard position (SL_FX_POSITIONS).
	FxFlagsCustom: false,
	FxFlagsStart: 1,
	FxFlagsCount: 5,
	FxFlagsCentre: true,                  // with the function Flags and a custom position: also light the top row
	FxSpotterCustom: false,
	FxSpotterStart: 1,
	FxSpotterCount: 5,
	FxLowFuelCustom: false,
	FxLowFuelStart: 1,
	FxLowFuelCount: 5,
	FxTcCustom: false,
	FxTcStart: 2,
	FxTcCount: 1,
	FxAbsCustom: false,
	FxAbsStart: 1,
	FxAbsCount: 1,
	FxHeadlightFlashCustom: false,
	FxHeadlightFlashStart: 1,
	FxHeadlightFlashCount: 1,
	FxTurnCustom: false,
	FxTurnStart: 5,
	FxTurnCount: 1,
	FxLiftCoastCustom: false,
	FxLiftCoastStart: 1,
	FxLiftCoastCount: 5,
	FxErsModeCustom: false,
	FxErsModeStart: 4,
	FxErsModeCount: 1,
	FxErsDeployCustom: false,
	FxErsDeployStart: 4,
	FxErsDeployCount: 1,
	FxErsChargeCustom: false,
	FxErsChargeStart: 4,
	FxErsChargeCount: 1,
	FxErsLapLimitCustom: false,
	FxErsLapLimitStart: 3,
	FxErsLapLimitCount: 1,
	FxP2pCustom: false,
	FxP2pStart: 4,
	FxP2pCount: 1,
	FxDrsCustom: false,
	FxDrsStart: 3,
	FxDrsCount: 1,
	// Test mode (no game needed): Off | Manual | Auto
	TestMode: 'Off',
	TestScenario: 0,               // Manual: index into SL_TEST_SCENARIOS
	TestRpmPercent: 80,            // Manual: RPM in % of an 8000 rpm redline
	TestBrakePercent: 0            // Manual: brake pedal in %
};

// Colour lists published by the plugin from the selected colour themes: "#RRGGBB,#RRGGBB,..." with 2 to 5 stops.
const SL_THEME_DEFAULTS = {
	RpmColours: '#00FF00,#FFFF00,#FF0000',
	IdleColours: '#FF8C00,#FF0000,#FF1493',
	FuelColours: '#FF0000,#00FF00'
};

// Working values, refreshed from the settings at the start of every frame by sl_loadConfig().
let SL_RPM_STYLE, SL_RPM_COLOURS, SL_RPM_COLOUR_MODE, SL_RPM_START, SL_RPM_END, SL_RPM_REDLINE, SL_RPM_FLASH_COLOUR, SL_RPM_FLASH_COLOUR2;
let SL_FUEL_PLACEMENT;
let SL_PEDAL_AREAS = {};
let SL_LAYERS = [];
let SL_LED_FUNCTION, SL_BRAKE_ENABLED, SL_FUEL_COLOURS, SL_FUEL_MODE, SL_BRIGHT_GENERAL, SL_BRIGHT_IDLE;
let SL_RPM_SHIFT_FLASH, SL_RPM_NO_TOP_FLASH;
let SL_BRAKE_AREA = 'both', SL_BRAKE_DIRECTION = 'outIn', SL_BRAKE_SMOOTH = false, SL_PEDALS_SMOOTH = false, SL_FUEL_SMOOTH = false;
let SL_CENTRE_FUNCTION, SL_BRAKE_COLOUR, SL_THROTTLE_COLOUR, SL_BRAKE_BLINK_FROM, SL_BRAKE_BLINK_FAST_FROM;
let SL_IDLE_ANIMATION, SL_IDLE_STYLE, SL_IDLE_SPEED, SL_IDLE_PALETTE;
let SL_SHOW_CLUTCH, SL_SHOW_ERS_REGEN, SL_SIDE_PIT;
let SL_TEST_MODE, SL_TEST_SCENARIO, SL_TEST_RPM, SL_TEST_BRAKE;
let SL_CFG = {};
let SL_FLAG = {};

// Effect colour setting -> entry of SL_C
const SL_FX_COLOURS = {
	FxSpotterColour: 'spotter', FxPitIndicatorColour: 'pitLane', FxPitEntryColour: 'pitEntry', FxPitExitColour: 'pitExit',
	FxPitSpeedingColour: 'pitSpeeding', FxPitLimiterColour1: 'pitLimiter1', FxPitLimiterColour2: 'pitLimiter2',
	FxEngineStartColour: 'engineStart', FxLowFuelColour: 'lowFuel', FxTcColour: 'tc', FxAbsColour: 'abs',
	FxHeadlightFlashColour: 'flasher', FxTurnColour: 'indicator', FxLiftCoastColour: 'liftCoast',
	FxErsDeployColour: 'ersDeploy', FxErsRegenColour: 'ersRegen', FxClutchColour: 'clutch', FxP2pColour: 'p2p',
	FxErsModeMediumColour: 'ersMedium', FxErsModeHotlapColour: 'ersHotlap', FxErsModeOvertakeColour: 'ersOvertake',
	FxScDeltaOkColour: 'scOk', FxScDeltaOffColour: 'scOff',
	FxDrsColour: 'drsOn', FxDrsDetectColour: 'drsDetect'
};

// Is the effect switched on? name is the part between "Fx" and "On" (for example 'Abs' for FxAbsOn).
// The group of the Effects page every effect belongs to; a group that is switched off silences all of its effects.
const SL_FX_GROUPS = {
	Flags: 'Indicators', Spotter: 'Indicators',
	PitIndicator: 'Pit', PitAlerts: 'Pit', PitSpeeding: 'Pit', PitLimiter: 'Pit',
	EngineStart: 'Car', LowFuel: 'Car', Tc: 'Car', Abs: 'Car', HeadlightFlash: 'Car', Turn: 'Car', LiftCoast: 'Car',
	ErsMode: 'Ers', ErsDeploy: 'Ers', ErsCharge: 'Ers', ErsLapLimit: 'Ers', P2p: 'Ers',
	Drs: 'Extras', DrsDetect: 'Extras', ScDelta: 'Extras'
};

// True unless the group is switched off (a missing setting counts as on, for presets from before the group switches).
function sl_group(group) {
	return SL_CFG['FxGroup' + group + 'On'] !== false;
}

function sl_fx(name) {
	return SL_CFG['Fx' + name + 'On'] === true && sl_group(SL_FX_GROUPS[name]);
}

function sl_hex(value, fallback) {
	return typeof value === 'string' && /^#[0-9A-Fa-f]{6}$/.test(value) ? '#FF' + value.slice(1).toUpperCase() : fallback;
}

// Parses a published colour list; anything invalid falls back to the default list.
function sl_colourList(key) {
	const raw = $prop('Shiftline.' + key);
	const list = typeof raw === 'string' ? raw.split(',').map(c => c.trim()) : [];
	const valid = list.length >= 2 && list.length <= 5 && list.every(c => /^#[0-9A-Fa-f]{6}$/.test(c));
	return (valid ? list : SL_THEME_DEFAULTS[key].split(',')).map(c => sl_hex(c));
}

function sl_loadConfig() {
	const cfg = {};
	for (const key in SL_DEFAULTS) {
		const def = SL_DEFAULTS[key];
		const raw = $prop('Shiftline.' + key);
		if (typeof def === 'boolean') cfg[key] = raw == null ? def : (raw === true || raw === 1 || raw === 'True' || raw === 'true');
		else if (typeof def === 'number') cfg[key] = raw != null && raw !== '' && isFinite(Number(raw)) ? Number(raw) : def;
		else if (/^#[0-9A-Fa-f]{6}$/.test(def)) cfg[key] = sl_hex(raw, sl_hex(def));
		else cfg[key] = raw == null || raw === '' ? def : String(raw);
	}
	SL_SIDE = Math.max(1, Math.min(SL_MAX_SIDE, Math.round(cfg.SideLeds)));
	SL_CENTRE = Math.max(2, Math.min(SL_MAX_CENTRE, Math.round(cfg.CentreLeds)));
	SL_TOTAL = SL_SIDE * 2 + SL_CENTRE;
	SL_RPM_STYLE = cfg.RpmStyle;
	SL_RPM_COLOURS = sl_colourList('RpmColours');
	SL_RPM_COLOUR_MODE = cfg.RpmColourMode;
	SL_RPM_START = Math.max(0, Math.min(1.5, cfg.RpmStartPercent / 100));
	SL_RPM_END = Math.max(SL_RPM_START + 0.01, Math.min(1.5, cfg.RpmEndPercent / 100));
	SL_RPM_REDLINE = ['flash', 'flashAlt', 'flashSwap'].includes(cfg.RpmRedline) ? cfg.RpmRedline : 'off';        // older 'solid' behaved like 'off'
	SL_RPM_FLASH_COLOUR = cfg.RpmFlashColour;
	SL_RPM_FLASH_COLOUR2 = cfg.RpmFlashColour2;
	SL_LED_FUNCTION = ['rpmBrake', 'rpmOnly', 'brakeOnly', 'brakeThrottle', 'fuel', 'flags'].includes(cfg.LedFunction) ? cfg.LedFunction : 'rpmBrake';
	SL_CENTRE_FUNCTION = { rpmBrake: 'rpm', rpmOnly: 'rpm', brakeOnly: 'none', brakeThrottle: 'throttleBrake', fuel: 'fuel', flags: 'none' }[SL_LED_FUNCTION];
	SL_BRAKE_ENABLED = SL_LED_FUNCTION === 'rpmBrake' || SL_LED_FUNCTION === 'brakeOnly';
	SL_FUEL_COLOURS = sl_colourList('FuelColours');
	SL_FUEL_MODE = cfg.FuelColourMode;
	SL_LAYERS = sl_layerOrder(cfg.LayerOrder, cfg.FxMasterOn);
	SL_PEDAL_AREAS = { brake: cfg.PedalBrake, throttle: cfg.PedalThrottle, clutch: cfg.PedalClutch };
	SL_FUEL_PLACEMENT = ['centre', 'sides', 'left', 'right'].includes(cfg.FuelPlacement) ? cfg.FuelPlacement : 'off';
	SL_BRIGHT_GENERAL = Math.max(1, Math.min(100, cfg.BrightnessGeneral)) / 100 * (cfg.NightMode ? 0.5 : 1);        // the idle brightness is applied first and scaled by this as well
	SL_RPM_SHIFT_FLASH = cfg.RpmShiftFlash;
	SL_RPM_NO_TOP_FLASH = cfg.RpmNoFlashInTopGear;
	SL_BRIGHT_IDLE = Math.max(1, Math.min(100, cfg.BrightnessIdle)) / 100;
	// The brake bar on the top row needs the centre LEDs, so it exists only with the function "brake pressure only"; otherwise the sides carry it.
	SL_BRAKE_AREA = ['both', 'left', 'right', 'top'].includes(cfg.BrakeArea) ? cfg.BrakeArea : 'both';
	if (SL_BRAKE_AREA === 'top' && SL_LED_FUNCTION !== 'brakeOnly') SL_BRAKE_AREA = 'both';
	SL_BRAKE_DIRECTION = ['outIn', 'leftRight', 'rightLeft', 'inOut'].includes(cfg.BrakeDirection) ? cfg.BrakeDirection : 'outIn';
	if (SL_BRAKE_AREA !== 'top' && (SL_BRAKE_DIRECTION === 'leftRight' || SL_BRAKE_DIRECTION === 'rightLeft')) SL_BRAKE_DIRECTION = 'outIn';   // left/right only makes sense on the top row
	SL_BRAKE_SMOOTH = cfg.BrakeSmooth === true;
	SL_PEDALS_SMOOTH = cfg.PedalsSmooth === true;
	SL_FUEL_SMOOTH = cfg.FuelSmooth === true;
	SL_BRAKE_COLOUR = cfg.BrakeColour;
	SL_THROTTLE_COLOUR = cfg.ThrottleColour;
	SL_BRAKE_BLINK_FROM = cfg.BrakeBlinkStage1;
	SL_BRAKE_BLINK_FAST_FROM = Math.max(cfg.BrakeBlinkStage1, cfg.BrakeBlinkStage2);
	SL_IDLE_ANIMATION = cfg.IdleEnabled;
	SL_IDLE_STYLE = cfg.IdleStyle;
	SL_IDLE_SPEED = cfg.IdleSpeed;
	SL_IDLE_PALETTE = sl_colourList('IdleColours');
	SL_SHOW_CLUTCH = cfg.ShowClutch;
	SL_SHOW_ERS_REGEN = cfg.ShowErsRegen;
	SL_SIDE_PIT = cfg.SidePit;
	SL_CFG = cfg;
	for (const key in SL_FX_COLOURS) SL_C[SL_FX_COLOURS[key]] = cfg[key];
	SL_FLAG = { red: cfg.FxFlagRedColour, black: cfg.FxFlagBlackColour, yellow: cfg.FxFlagYellowColour, white: cfg.FxFlagWhiteColour,
		orange: cfg.FxFlagOrangeColour, blue: cfg.FxFlagBlueColour, green: cfg.FxFlagGreenColour, checkered: cfg.FxFlagCheckeredColour };
	SL_TEST_MODE = cfg.TestMode;
	SL_TEST_SCENARIO = cfg.TestScenario;
	SL_TEST_RPM = cfg.TestRpmPercent;
	SL_TEST_BRAKE = cfg.TestBrakePercent;
}

const SL_IDLE_STYLES = ['wave', 'sweep', 'comet', 'fillDrain', 'centreOut', 'solid', 'colourFade',
	'breathe', 'bounce', 'orbit', 'theatreChase', 'alternate', 'wipe', 'rain', 'ripple', 'twinkle'];

const SL_CHECKERED_FLAG_MS = 10000;
const SL_WHITE_LAST_LAP_MS = 10000;
const SL_LOWFUEL_MS = 10000;        // low fuel chase runs this long after the alert starts
const SL_ENGINE_START_MS = 2000;
const SL_IDLE_MS = 5000;
const SL_PIT_EXIT_MS = 10000;

const SL_C = {
	abs: '#FFFFFF00', tc: '#FF00BFFF',
	clutch: '#FF8A2BE2', flasher: '#FF00BFFF', indicator: '#FFFFA500', spotter: '#FFFF1493',
	lowFuel: '#FFFF0000', liftCoast: '#FFBF00FF', p2p: '#FF008000',
	ersDeploy: '#FF0000FF', ersRegen: '#FFFF1493',
	ersMedium: '#FF00C060', ersHotlap: '#FFFF8C00', ersOvertake: '#FFFF0000', scOk: '#FF00FF00', scOff: '#FFFFA500',
	drsOn: '#FF008000', drsDetect: '#FFFFFF00',
	pitLimiter1: '#FF0000FF', pitLimiter2: '#FFFFFFFF', pitEntry: '#FFFF0000', pitExit: '#FF00FF00',
	pitLane: '#FFFFFF00', pitSpeeding: '#FFFF0000', engineStart: '#FFFF4500', white: '#FFFFFFFF', grey: '#FF808080',
	yellow: '#FFFFFF00', red: '#FFFF0000', orange: '#FFFF8C00', blue: '#FF0000FF', green: '#FF00FF00'
};

// Colours of the level displays (ERS state of charge, energy left for the lap): green, yellow, orange, red.
const SL_LEVEL = { high: '#FF008000', yellow: '#FFFFFF00', orange: '#FFFF8C00', red: '#FFFF0000' };

const SL_F1_GAMES = ['F12030', 'F12029', 'F12028', 'F12027', 'F12026', 'F12025', 'F12024', 'F12023', 'F12022', 'F12021'];

// ============================== test mode ======================================
// TestMode = Manual runs the profile on the scenario / RPM / brake chosen on the plugin page
// instead of game data (SimHub's own test data editor does not feed script layers).
// TestMode = Auto loops through all scenarios.

const SL_TEST_STEP_MS = 3000;
const SL_F1 = { 'DataCorePlugin.CurrentGame': 'F12025' };
const SL_TEST_SCENARIOS = [
	['Idle (configured style)', { 'DataCorePlugin.GameRunning': 0 }]
].concat(SL_IDLE_STYLES.map(style => ['Idle: ' + style, { 'DataCorePlugin.GameRunning': 0, '__idleStyle': style }]), [
	['RPM and brake (use the sliders)', { __sliders: true }],
	['Redline reached (rev limiter)', { Rpms: 8300 }],
	['Brake pedal sweep', { __brakeSweep: true }],
	['Spotter left (close)', { SpotterCarLeft: 1, SpotterCarLeftDistance: 2 }],
	['Spotter right (far)', { SpotterCarRight: 1, SpotterCarRightDistance: 8 }],
	['Yellow flag', { Flag_Yellow: 1 }],
	['Blue flag', { Flag_Blue: 1 }],
	['White flag', { Flag_White: 1 }],
	['Orange flag', { Flag_Orange: 1 }],
	['Green flag', { Flag_Green: 1 }],
	['Checkered flag', { Flag_Checkered: 1 }],
	['Black flag', { Flag_Black: 1 }],
	['ERS mode: medium (F1)', Object.assign({ 'GameRawData.PlayerCarStatusData.m_ersDeployMode': 1 }, SL_F1)],
	['ERS mode: overtake (F1)', Object.assign({ 'GameRawData.PlayerCarStatusData.m_ersDeployMode': 3 }, SL_F1)],
	['Safety car delta: too slow (F1)', Object.assign({ 'DataCorePlugin.GameRawData.PacketSessionData.m_safetyCarStatus': 1, 'GameRawData.PlayerLapData.m_safetyCarDelta': 1.2 }, SL_F1)],
	['Safety car (F1)', { 'DataCorePlugin.CurrentGame': 'F12025', 'DataCorePlugin.GameRawData.PacketSessionData.m_safetyCarStatus': 1 }],
	['ABS and TC active', { ABSActive: 1, TCActive: 1 }],
	['Indicator left', { TurnIndicatorLeft: 1 }],
	['Indicator right', { TurnIndicatorRight: 1 }],
	['Hazard lights', { TurnIndicatorLeft: 1, TurnIndicatorRight: 1 }],
	['DRS available', { DRSAvailable: 1 }],
	['DRS open', { DRSEnabled: 1 }],
	['ERS charge 70 % (F1)', Object.assign({ ERSPercent: 70 }, SL_F1)],
	['ERS charge 8 % (F1)', Object.assign({ ERSPercent: 8 }, SL_F1)],
	['ERS lap limit 50 % (F1)', Object.assign({ ERSMax: 4000000, 'GameRawData.PlayerCarStatusData.m_ersDeployedThisLap': 2000000 }, SL_F1)],
	['Push to pass', { PushToPassActive: 1 }],
	['Headlight flash (ACC)', { 'DataCorePlugin.CurrentGame': 'AssettoCorsaCompetizione', 'DataCorePlugin.GameRawData.Graphics.FlashingLights': 1 }],
	['Lift and coast (LMU)', { 'DataCorePlugin.CurrentGame': 'LMU', 'GameRawData.PlayerNativeTelemetry.mLiftAndCoastProgress': 160 }],
	['Low fuel', { CarSettings_FuelAlertActive: 1 }],
	['Pit lane speeding (LMU)', { IsInPitLane: 1, SpeedKmh: 80, PitLimiterOn: 0, 'DataCorePlugin.CurrentGame': 'LMU' }],
	['Pit limiter in pit lane', { IsInPitLane: 1, SpeedKmh: 60, PitLimiterOn: 1 }],
	['Pit exit with limiter', { IsInPitLane: 0, SpeedKmh: 60, PitLimiterOn: 1 }],
	['Engine start', { EngineStarted: 1 }],
	['Pit lane indicator', { IsInPitLane: 1 }],
	['Pit lane entry without limiter (LMU)', { IsInPitLane: 1, SpeedKmh: 10, PitLimiterOn: 0, 'DataCorePlugin.CurrentGame': 'LMU' }],
	['ERS deploy (LMU)', { 'DataCorePlugin.CurrentGame': 'LMU', 'GameRawData.CurrentPlayerTelemetry.mElectricBoostMotorState': 2, 'GameRawData.CurrentPlayerTelemetry.mElectricBoostMotorTorque': 5 }],
	['ERS regeneration (LMU)', { 'DataCorePlugin.CurrentGame': 'LMU', 'GameRawData.CurrentPlayerTelemetry.mElectricBoostMotorState': 3 }],
	['DRS detection point (iRacing)', { 'DataCorePlugin.CurrentGame': 'IRacing', 'GameRawData.Telemetry.DRS_Status': 1 }],
	['Clutch pedal', { Clutch: 60 }],
	// Sweeps for the play buttons of the LED page: each one is periodic over 3 seconds, so a 3 second test shows the whole range whenever it starts.
	['RPM sweep', { __rpmSweep: true }],
	['Pedal sweep', { __brakeSweep: true, __pedalSweep: true }],
	['Fuel sweep', { __fuelSweep: true }],
	['All LEDs on', { __allOn: true }]
]);

function sl_testActive() {
	return SL_TEST_MODE === 'Manual' || SL_TEST_MODE === 'Auto';
}

function sl_p(name) {
	if (!sl_testActive()) return $prop(name);
	const manual = SL_TEST_MODE === 'Manual';
	const count = SL_TEST_SCENARIOS.length;
	const index = manual ? SL_TEST_SCENARIO : Math.floor(Date.now() / SL_TEST_STEP_MS);
	const props = SL_TEST_SCENARIOS[((index % count) + count) % count][1];
	const now = Date.now();
	if (name === 'CarSettings_RedLineRPM') return 8000;
	const cycle = 0.5 - 0.5 * Math.cos(now / 3000 * 2 * Math.PI);        // 0 -> 1 -> 0 once every 3 seconds
	if (name === 'Rpms' && props.__rpmSweep) {        // from just below the first LED up to 105 % of the redline, so that none of the 3 seconds is wasted
		const first = Number(SL_CFG.RpmStartPercent);
		const low = Math.max(0, (isFinite(first) ? first : 70) - 5);
		return 8000 * (low + (105 - low) * cycle) / 100;
	}
	if (name === 'Throttle' && props.__pedalSweep) return 100 * (0.5 - 0.5 * Math.cos((now / 3000 + 1 / 3) * 2 * Math.PI));
	if (name === 'Clutch' && props.__pedalSweep) return 100 * (0.5 - 0.5 * Math.cos((now / 3000 + 2 / 3) * 2 * Math.PI));
	if (name === 'FuelPercent' && props.__fuelSweep) return 100 * (1 - cycle);        // full -> empty -> full
	if (name === 'Rpms') return props.__sliders ? (manual ? 8000 * SL_TEST_RPM / 100 : 8000 * (0.6 + 0.46 * (0.5 - 0.5 * Math.cos(now / 5000 * 2 * Math.PI)))) : props.Rpms;
	if (name === 'Brake') return props.__sliders && manual ? SL_TEST_BRAKE : (props.__brakeSweep ? 100 * (0.5 - 0.5 * Math.cos(now / 3000 * 2 * Math.PI)) : 0);
	if (name === 'Throttle') return 50 + 50 * Math.sin(now / 1500);
	if (name === 'Clutch') return 50 + 50 * Math.sin(now / 1000);
	if (name === 'FuelPercent') return 50 + 50 * Math.sin(now / 4000);
	return props[name];
}

// ============================== helpers ========================================

function sl_game() { return sl_p('DataCorePlugin.CurrentGame'); }
function sl_isF1() { return SL_F1_GAMES.includes(sl_game()); }
function sl_tick(halfPeriodMs) { return Math.floor(Date.now() / halfPeriodMs) % 2; }

function sl_safe(fn) {
	try { return fn(); } catch (e) { return null; }
}

function sl_blank() { return Array(SL_TOTAL).fill(null); }

// Slot on the left / right / both sides, 0 = outermost.
// Slots outside 0..SL_SIDE-1 are ignored, so a small side never writes into the centre.
function sl_setLeft(frame, slot, colour) { if (slot >= 0 && slot < SL_SIDE) frame[slot] = colour; }
function sl_setRight(frame, slot, colour) { if (slot >= 0 && slot < SL_SIDE) frame[SL_TOTAL - 1 - slot] = colour; }

// Slot of every side role. For 5 LEDs per side: ABS 0, TC 1, DRS 2, ERS 3, indicator 4.
function sl_slots() {
	const last = SL_SIDE - 1;
	return { abs: 0, tc: Math.min(1, last), drs: Math.max(0, last - 2), ers: Math.max(0, last - 1), indicator: last };
}
// Standard position of every side effect for a side with S LEDs: [start (1 = outermost), count]. The plugin gets the same
// table generated into Settings.cs; with a custom position the settings FxXxxStart / FxXxxCount replace it.
const SL_FX_POSITIONS = {
	Flags: S => [1, S], Spotter: S => [1, S], LowFuel: S => [1, S], LiftCoast: S => [1, S],
	Abs: S => [1, 1], HeadlightFlash: S => [1, 1], Tc: S => [Math.min(2, S), 1],
	Drs: S => [Math.max(1, S - 2), 1], ErsLapLimit: S => [Math.max(1, S - 2), 1],
	ErsMode: S => [Math.max(1, S - 1), 1], ErsCharge: S => [Math.max(1, S - 1), 1], ErsDeploy: S => [Math.max(1, S - 1), 1], P2p: S => [Math.max(1, S - 1), 1],
	Turn: S => [S, 1]
};

// The side LEDs an effect uses: { first (slot, 0 = outermost), len }.
function sl_fxRange(name) {
	let [start, len] = SL_FX_POSITIONS[name](SL_SIDE);
	if (SL_CFG['Fx' + name + 'Custom'] === true) {
		start = Math.round(Number(SL_CFG['Fx' + name + 'Start']));
		len = Math.round(Number(SL_CFG['Fx' + name + 'Count']));
	}
	start = Math.max(1, Math.min(SL_SIDE, isFinite(start) ? start : 1));
	len = Math.max(1, Math.min(SL_SIDE - start + 1, isFinite(len) ? len : 1));
	return { first: start - 1, len };
}

// Paints colour(k) on every LED of the range of an effect (k = position inside the range, 0 = outermost) on the chosen sides.
function sl_setRange(frame, name, colour, left = true, right = true) {
	const { first, len } = sl_fxRange(name);
	for (let k = 0; k < len; k++) {
		const c = typeof colour === 'function' ? colour(k, len) : colour;
		if (c == null) continue;
		if (left) sl_setLeft(frame, first + k, c);
		if (right) sl_setRight(frame, first + k, c);
	}
}

function sl_setBoth(frame, slot, colour) { sl_setLeft(frame, slot, colour); sl_setRight(frame, slot, colour); }

// Writes an array of SL_SIDE colours (index 0 = outermost) to both sides.
function sl_setSides(frame, arr) {
	for (let i = 0; i < SL_SIDE; i++) {
		if (arr[i] != null) sl_setBoth(frame, i, arr[i]);
	}
}

// Writes up to SL_CENTRE colours into the centre.
function sl_setCentre(frame, arr) {
	for (let i = 0; i < SL_CENTRE && i < arr.length; i++) {
		if (arr[i] != null) frame[SL_SIDE + i] = arr[i];
	}
}

function sl_paint(frame, layer) {
	if (!layer) return;
	for (let i = 0; i < SL_TOTAL && i < layer.length; i++) {
		if (layer[i] != null) frame[i] = layer[i];
	}
}

function sl_mix(colour1, colour2, ratio) {
	const a = parseInt(colour1.slice(1), 16), b = parseInt(colour2.slice(1), 16);
	const ch = shift => Math.round(((a >>> shift) & 0xff) + ratio * (((b >>> shift) & 0xff) - ((a >>> shift) & 0xff)));
	const hex = v => Math.max(0, Math.min(255, v)).toString(16).padStart(2, '0');
	return ('#' + hex(ch(24)) + hex(ch(16)) + hex(ch(8)) + hex(ch(0))).toUpperCase();
}

// True while triggerTime lies at most durationMs in the past (a clock stepping backwards counts as expired).
function sl_within(triggerTime, durationMs) {
	if (triggerTime === null) return false;
	const age = Date.now() - triggerTime;
	return age >= 0 && age <= durationMs;
}

// True for durationMs after `active` switched from false to true.
const sl_pulseStates = {};
function sl_pulse(key, active, durationMs) {
	const state = sl_pulseStates[key] ??= { old: false, triggerTime: null };
	if (active && !state.old) state.triggerTime = Date.now();
	state.old = !!active;
	return sl_within(state.triggerTime, durationMs);
}

// True for durationMs after a numeric value increased (dir > 0) or decreased (dir < 0).
const sl_edgeStates = {};
function sl_edge(key, value, dir, durationMs) {
	const state = sl_edgeStates[key] ??= { old: value, triggerTime: null };
	if (dir > 0 ? value > state.old : value < state.old) state.triggerTime = Date.now();
	state.old = value;
	return sl_within(state.triggerTime, durationMs);
}

// Telemetry that depends on the game ------------------------------------------------

// Brake pressure 0..100 from the least filtered source of each game.
function sl_brakePct() {
	const g = sl_game();
	const raw = {
		AssettoCorsaCompetizione: 'GameRawData.Physics.Brake',
		AssettoCorsaEVO: 'GameRawData.Physics.brake',
		IRacing: 'GameRawData.Telemetry.BrakeRaw',
		AssettoCorsa: 'GameRawData.Physics.Brake',
		Automobilista2: 'GameRawData.mUnfilteredBrake',
		LMU: 'GameRawData.PlayerNativeTelemetry.mUnfilteredBrake',
		RFactor2: 'GameRawData.CurrentPlayerTelemetry.mUnfilteredBrake',
		RRRE: 'GameRawData.BrakeRaw'
	}[g];
	if (raw) return parseInt(sl_p(raw) * 100) || 0;
	return parseInt(sl_p('Brake')) || 0;
}

function sl_pitSpeedLimit() {
	const g = sl_game();
	if (g === 'AssettoCorsaCompetizione') return 50;
	if (g === 'IRacing') return parseInt(sl_p('GameRawData.SessionData.WeekendInfo.TrackPitSpeedLimit'));
	if (sl_isF1()) return sl_p('GameRawData.PacketSessionData.m_pitSpeedLimit') || 0;
	if (g === 'LMU') return 60;
	if (g === 'RRRE') return sl_p('GameRawData.SessionPitSpeedLimit') * 3.6;
	return undefined;
}

// ERS state of charge in % (undefined when the car has none).
function sl_ersSoC() {
	const g = sl_game();
	if (g === 'LMU' && sl_p('GameRawData.CurrentPlayerTelemetry.mElectricBoostMotorState') != 0) {
		return sl_p('GameRawData.PlayerNativeTelemetry.mSoC');
	}
	if (g === 'AssettoCorsa' && sl_p('GameRawData.StaticInfo.HasERS') == 1) return sl_p('GameRawData.Physics.KersCharge') * 100;
	if (g === 'AssettoCorsaEVO' && sl_p('GameRawData.Graphics.has_kers') == true) return sl_p('GameRawData.Graphics.kers_charge_perc') * 100;
	if (g === 'Automobilista2' && (sl_p('GameRawData.mErsDeploymentMode') != 0 || sl_p('GameRawData.mErsAutoModeEnabled') == true)) return sl_p('ERSPercent');
	if (g === 'IRacing' && sl_p('GameRawData.Telemetry.EnergyERSBatteryPct') != null) return sl_p('GameRawData.Telemetry.EnergyERSBatteryPct') * 100;
	if (sl_isF1()) return sl_p('ERSPercent');
	if (g === 'RRRE' && sl_p('GameRawData.BatterySoC') != -1) return sl_p('GameRawData.BatterySoC');
	return undefined;
}

function sl_ersDeploying() {
	const g = sl_game();
	if (g === 'LMU') {
		return sl_p('GameRawData.CurrentPlayerTelemetry.mElectricBoostMotorState') == 2 &&
			sl_p('GameRawData.CurrentPlayerTelemetry.mElectricBoostMotorTorque') > 0;
	}
	if (g === 'AssettoCorsa') {
		return (sl_p('GameRawData.StaticInfo.HasERS') == 1 || sl_p('GameRawData.StaticInfo.HasKERS') == 1) &&
			sl_p('GameRawData.Physics.KersInput') > 0;
	}
	if (g === 'AssettoCorsaEVO') {
		return sl_p('GameRawData.Graphics.has_kers') == true &&
			sl_edge('evoDeploy', sl_p('GameRawData.Graphics.kers_charge_perc'), -1, 100);
	}
	if (g === 'Automobilista2') return sl_edge('ams2Deploy', sl_p('ERSPercent'), -1, 100);
	if (g === 'IRacing') return sl_edge('irDeploy', sl_p('GameRawData.Telemetry.EnergyERSBatteryPct'), -1, 100);
	if (sl_isF1()) return sl_edge('f1Deploy', sl_p('GameRawData.PlayerCarStatusData.m_ersDeployedThisLap'), 1, 100);
	return false;
}

function sl_ersRegenerating() {
	const g = sl_game();
	if (g === 'LMU') return sl_p('GameRawData.CurrentPlayerTelemetry.mElectricBoostMotorState') == 3;
	if (g === 'AssettoCorsa') return sl_p('GameRawData.StaticInfo.HasERS') == 1 && sl_p('GameRawData.Physics.ErsisCharging') == 1;
	if (g === 'AssettoCorsaEVO') return sl_p('GameRawData.Graphics.has_kers') == true && sl_p('GameRawData.Graphics.kers_is_charging') == true;
	if (g === 'Automobilista2') return sl_edge('ams2Regen', sl_p('ERSPercent'), 1, 100);
	if (g === 'IRacing') return sl_edge('irRegen', sl_p('GameRawData.Telemetry.EnergyERSBatteryPct'), 1, 100);
	return false;
}

// KERS / ERS energy left for this lap in % (undefined when not applicable).
function sl_kersLeft() {
	const g = sl_game();
	if (g === 'AssettoCorsa' && (sl_p('GameRawData.StaticInfo.HasKERS') == 1 || sl_p('GameRawData.StaticInfo.HasERS') == 1)) {
		return 100 - sl_p('ERSPercent');
	}
	if (g === 'Automobilista2' && sl_p('GameRawData.mErsAutoModeEnabled') == true) {
		return sl_p('GameRawData.mBoostActive') == true ? 100 : 0;
	}
	if (sl_isF1()) {
		return 100 - sl_p('GameRawData.PlayerCarStatusData.m_ersDeployedThisLap') / sl_p('ERSMax') * 100;
	}
	return undefined;
}

function sl_drsDetectionPoint() {
	const g = sl_game();
	return (g === 'IRacing' && sl_p('GameRawData.Telemetry.DRS_Status') == 1) ||
		(g === 'Automobilista2' && (sl_p('GameRawData.mDrsState') & (1 << 2)) !== 0) ||
		(sl_isF1() && sl_p('GameRawData.PlayerCarStatusData.m_drsActivationDistance') > 0);
}

function sl_flashingHeadlights() {
	const g = sl_game();
	return (g === 'AssettoCorsaCompetizione' && sl_p('DataCorePlugin.GameRawData.Graphics.FlashingLights') == 1) ||
		(g === 'AssettoCorsaEVO' && sl_p('GameRawData.Graphics.instrumentation.flashing_lights') == true) ||
		(g === 'IRacing' && sl_pulse('irFlash', sl_p('GameRawData.Telemetry.dcHeadlightFlash') == true, 1500));
}

// ============================== centre layers ==================================

// Colour of the k-th lit step (0..n-1) of the rev bar.
function sl_revColour(k, n) {
	const c = SL_RPM_COLOURS;
	if (SL_RPM_STYLE === 'f1' && c.length === 3) return c[k < 5 ? 0 : k < 10 ? 1 : 2];             // 5 / 5 / 6 blocks
	if (SL_RPM_COLOUR_MODE === 'gradient') {
		const x = n > 1 ? k / (n - 1) * (c.length - 1) : 0;
		const i = Math.min(c.length - 2, Math.floor(x));
		return sl_mix(c[i], c[i + 1], x - i);
	}
	return c[Math.min(c.length - 1, Math.floor(k / n * c.length))];
}

// LED indices inside the centre that light at step k, and the number of steps.
function sl_revSteps() {
	const L = SL_CENTRE, lo = Math.floor((L - 1) / 2), hi = Math.ceil((L - 1) / 2);   // middle LED(s), also for odd L
	const uniq = list => list.filter((v, i) => list.indexOf(v) === i);
	switch (SL_RPM_STYLE) {
		case 'middle':    return { n: lo + 1, leds: k => uniq([k, L - 1 - k]) };
		case 'centreOut': return { n: lo + 1, leds: k => uniq([lo - k, hi + k]) };
		case 'rightLeft': return { n: L, leds: k => [L - 1 - k] };
		default:          return { n: L, leds: k => [k] };      // 'leftRight' and 'f1'
	}
}

// True in the highest gear of the car. SimHub publishes the number of gears per car (learned from the highest gear used when the game does not say).
function sl_inTopGear() {
	const max = Number(sl_p('CarSettings_MaxGears')), gear = parseInt(sl_p('Gear'), 10);
	return max > 0 && gear >= max;
}

function sl_revLayer() {
	const rpm = sl_p('Rpms');
	const redline = sl_p('CarSettings_RedLineRPM');
	if (!(rpm > 0) || !(redline > 0)) return null;

	const { n, leds } = sl_revSteps();
	const centre = Array(SL_CENTRE).fill(null);
	// 'simhub' style: the bar follows the shift points set per car in SimHub (Cars settings) instead of the start and
	// end percentages of this plugin. SimHub publishes them as two zones with a progress of 0 to 1 each.
	const zone1 = sl_p('CarSettings_RPMShiftLight1'), zone2 = sl_p('CarSettings_RPMShiftLight2');
	const useSimhub = SL_RPM_STYLE === 'simhub' && typeof zone1 === 'number' && typeof zone2 === 'number';
	let atRedline = rpm >= redline || ((useSimhub || SL_RPM_SHIFT_FLASH) && sl_p('CarSettings_RPMRedLineReached') == 1);
	if (atRedline && SL_RPM_NO_TOP_FLASH && sl_inTopGear()) atRedline = false;       // nothing to shift into: the bar just stays full
	if (atRedline && SL_RPM_REDLINE === 'flash') {
		centre.fill(sl_tick(60) ? SL_RPM_FLASH_COLOUR : SL_OFF);
	} else if (atRedline && SL_RPM_REDLINE === 'flashSwap') {           // all LEDs in colour 1, then all in colour 2
		centre.fill(sl_tick(60) ? SL_RPM_FLASH_COLOUR : SL_RPM_FLASH_COLOUR2);
	} else if (atRedline && SL_RPM_REDLINE === 'flashAlt') {            // every second LED in the other colour, swapping quickly
		const swap = sl_tick(60) === 0;
		for (let i = 0; i < SL_CENTRE; i++) centre[i] = (i % 2 === 0) === swap ? SL_RPM_FLASH_COLOUR : SL_RPM_FLASH_COLOUR2;
	} else if (useSimhub) {
		const progress = Math.max(0, Math.min(1, zone2 > 0 ? 0.5 + 0.5 * zone2 : 0.5 * zone1));    // zone 1 fills the first half, zone 2 the second
		const lit = Math.ceil(progress * n - 0.0001);
		for (let k = 0; k < lit && k < n; k++) {
			for (const led of leds(k)) centre[led] = sl_revColour(k, n);
		}
	} else {
		const step = (SL_RPM_END - SL_RPM_START) / Math.max(1, n - 1);
		for (let k = 0; k < n; k++) {
			if (rpm < redline * (SL_RPM_START + k * step)) break;
			for (const led of leds(k)) centre[led] = sl_revColour(k, n);
		}
	}
	const frame = sl_blank();
	sl_setCentre(frame, centre);
	return frame;
}

// Brightness of every LED of a bar that is filled to `exact` LEDs (0..max). Smooth: the LED that is just coming on fades in (a level
// between 0 and 1); otherwise whole LEDs only, rounded up (ceil) or to the nearest LED.
function sl_levels(exact, max, smooth, ceil) {
	const x = Math.max(0, Math.min(max, exact || 0));
	if (!smooth) return Array(Math.min(max, ceil ? Math.ceil(x - 1e-9) : Math.round(x))).fill(1);
	const full = Math.floor(x + 1e-9), part = x - full;
	const levels = Array(full).fill(1);
	if (part > 0.001 && full < max) levels.push(part);
	return levels;
}
function sl_level(colour, level) { return level >= 1 ? colour : sl_mix(SL_OFF, colour, level); }

// Fills one pedal (value 0..1) into its area of the strip.
function sl_pedalFill(frame, area, value, colour) {
	const L = SL_CENTRE, half = Math.floor(L / 2);
	const v = Math.max(0, Math.min(1, value || 0));
	const lv = (n) => sl_levels(v * n, n, SL_PEDALS_SMOOTH, false);
	const centre = (i, level) => { if (i >= 0 && i < L) frame[SL_SIDE + i] = sl_level(colour, level); };
	const side = (put, n) => lv(n).forEach((level, i) => put(frame, i, sl_level(colour, level)));
	switch (area) {
		case 'centreMiddle': {
			const lo = Math.floor((L - 1) / 2), hi = Math.ceil((L - 1) / 2);
			lv(lo + 1).forEach((level, k) => { centre(lo - k, level); centre(hi + k, level); });
			break;
		}
		case 'centreLeft': lv(L).forEach((level, i) => centre(i, level)); break;
		case 'centreRight': lv(L).forEach((level, i) => centre(L - 1 - i, level)); break;
		case 'halfLeft': lv(half).forEach((level, i) => centre(half - 1 - i, level)); break;
		case 'halfRight': lv(L - half).forEach((level, i) => centre(half + i, level)); break;
		case 'centreEnds': lv(half).forEach((level, i) => { centre(i, level); centre(L - 1 - i, level); }); break;
		case 'sideLeft': side(sl_setLeft, SL_SIDE); break;
		case 'sideRight': side(sl_setRight, SL_SIDE); break;
		case 'sideBoth': side(sl_setBoth, SL_SIDE); break;
	}
}

// The pedal display: clutch, then throttle, then brake painted over each other.
function sl_throttleBrakeLayer() {
	const frame = sl_blank();
	sl_pedalFill(frame, SL_PEDAL_AREAS.clutch, (sl_p('Clutch') || 0) / 100, SL_C.clutch);
	sl_pedalFill(frame, SL_PEDAL_AREAS.throttle, (sl_p('Throttle') || 0) / 100, SL_THROTTLE_COLOUR);
	sl_pedalFill(frame, SL_PEDAL_AREAS.brake, sl_brakePct() / 100, SL_BRAKE_COLOUR);
	return frame;
}

// Colour at position t (0..1) of a colour list: smooth blend or solid blocks.
function sl_sampleList(list, t, mode) {
	const x = Math.max(0, Math.min(1, t));
	if (mode === 'gradient' && list.length > 1) {
		const k = x * (list.length - 1), i = Math.min(list.length - 2, Math.floor(k));
		return sl_mix(list[i], list[i + 1], k - i);
	}
	return list[Math.min(list.length - 1, Math.floor(x * list.length))];
}

function sl_fuelLayer() {
	const pct = sl_p('FuelPercent');
	if (!(pct >= 0)) return null;
	const centre = Array(SL_CENTRE).fill(null);
	sl_levels(pct / 100 * SL_CENTRE, SL_CENTRE, SL_FUEL_SMOOTH, true).forEach((level, i) => {
		centre[i] = sl_level(sl_sampleList(SL_FUEL_COLOURS, SL_CENTRE > 1 ? i / (SL_CENTRE - 1) : 1, SL_FUEL_MODE), level);
	});
	const frame = sl_blank();
	sl_setCentre(frame, centre);
	return frame;
}

// Fuel level in a chosen area (centre, both sides, left or right). The fuel level has the lowest priority: it only shows while no
// other layer lights any LED of that area, and it disappears completely as soon as one does (brake bar, flag, effect, ...).
// Layers that blink write opaque black for their off phase, so the area stays taken and the fuel level does not flicker in.
function sl_areaFree(frame, from, to) {
	for (let i = from; i <= to; i++) if (frame[i] != null) return false;
	return true;
}

function sl_fuelOverlay(frame) {
	const place = SL_FUEL_PLACEMENT;
	if (place === 'off' || (place === 'centre' && SL_LED_FUNCTION === 'fuel')) return;       // with function Fuel the centre is drawn by the function itself
	const pct = sl_p('FuelPercent');
	if (!(pct >= 0)) return;
	const left = [0, SL_SIDE - 1], right = [SL_TOTAL - SL_SIDE, SL_TOTAL - 1], centre = [SL_SIDE, SL_SIDE + SL_CENTRE - 1];
	const areas = place === 'sides' ? [left, right] : place === 'left' ? [left] : place === 'right' ? [right] : [centre];
	if (!areas.every(area => sl_areaFree(frame, area[0], area[1]))) return;

	if (place === 'centre') {
		sl_levels(pct / 100 * SL_CENTRE, SL_CENTRE, SL_FUEL_SMOOTH, true).forEach((level, i) => {
			frame[SL_SIDE + i] = sl_level(sl_sampleList(SL_FUEL_COLOURS, SL_CENTRE > 1 ? i / (SL_CENTRE - 1) : 1, SL_FUEL_MODE), level);
		});
		return;
	}
	sl_levels(pct / 100 * SL_SIDE, SL_SIDE, SL_FUEL_SMOOTH, true).forEach((level, i) => {            // fills from the outermost LED inwards
		const colour = sl_level(sl_sampleList(SL_FUEL_COLOURS, SL_SIDE > 1 ? i / (SL_SIDE - 1) : 1, SL_FUEL_MODE), level);
		if (place !== 'right') sl_setLeft(frame, i, colour);
		if (place !== 'left') sl_setRight(frame, i, colour);
	});
}

function sl_centreLayer() {
	if (SL_CENTRE_FUNCTION === 'throttleBrake') return sl_throttleBrakeLayer();
	if (SL_CENTRE_FUNCTION === 'fuel') return SL_FUEL_PLACEMENT === 'centre' ? sl_fuelLayer() : null;        // function Fuel: "Show fuel level on" decides where; Off shows nothing
	if (SL_CENTRE_FUNCTION === 'none') return null;
	return sl_revLayer();
}

function sl_clutchLayer() {
	if (!SL_SHOW_CLUTCH || !sl_group('Extras')) return null;
	const lit = Math.round((sl_p('Clutch') || 0) / 100 * SL_CENTRE);
	if (lit <= 0) return null;
	const frame = sl_blank();
	sl_setCentre(frame, Array(SL_CENTRE).fill(null).map((_, i) => i < lit ? SL_C.clutch : null));
	return frame;
}

// Brake bar in the centre (non-default modes) -------------------------------------------

function sl_brakeCentreLayer(pct) {
	const len = SL_CENTRE;
	const lit = Math.min(len, Math.ceil(pct / 100 * len));
	if (lit <= 0) return null;
	const blinking = pct >= SL_BRAKE_BLINK_FROM;
	const blinkOn = sl_tick(pct >= SL_BRAKE_BLINK_FAST_FROM ? 75 : 150) === 0;
	const blinkFrom = Math.floor(SL_BRAKE_BLINK_FROM / 100 * len);
	const smooth = SL_BRAKE_SMOOTH && !blinking;
	const bar = Array(len).fill(null);
	const put = (index, order, level) => {
		bar[index] = (blinking && order >= blinkFrom) ? (blinkOn ? SL_BRAKE_COLOUR : SL_OFF) : sl_level(SL_BRAKE_COLOUR, level);
	};
	const half = Math.floor(len / 2);
	const halfLit = Math.ceil(lit / 2);
	const one = smooth ? sl_levels(pct / 100 * len, len, true) : Array(lit).fill(1);                         // one level per LED
	const two = smooth ? sl_levels(pct / 100 * len / 2, half, true) : Array(Math.min(halfLit, half)).fill(1); // one level per pair
	if (SL_BRAKE_DIRECTION === 'leftRight') one.forEach((level, i) => put(i, i, level));
	else if (SL_BRAKE_DIRECTION === 'rightLeft') one.forEach((level, i) => put(len - 1 - i, i, level));
	else if (SL_BRAKE_DIRECTION === 'inOut') {
		two.forEach((level, i) => { put(half - 1 - i, i * 2, level); put(half + i, i * 2, level); });
	} else { // centre-outside-in
		two.forEach((level, i) => { put(i, i * 2, level); put(len - 1 - i, i * 2, level); });
	}
	const frame = sl_blank();
	sl_setCentre(frame, bar);
	return frame;
}

// Brake bar on the side LEDs, mirrored ----------------------------------------------

function sl_brakeSidesLayer(pct) {
	const len = SL_SIDE;                                 // all LEDs of the chosen side(s)
	const blinking = pct >= SL_BRAKE_BLINK_FROM;
	const levels = sl_levels(pct / 100 * len, len, SL_BRAKE_SMOOTH && !blinking, true);
	if (levels.length <= 0) return null;
	const blinkOn = sl_tick(pct >= SL_BRAKE_BLINK_FAST_FROM ? 75 : 150) === 0;
	const frame = sl_blank();
	levels.forEach((level, i) => {
		const colour = blinking ? (blinkOn ? SL_BRAKE_COLOUR : SL_OFF) : sl_level(SL_BRAKE_COLOUR, level);
		const slot = SL_BRAKE_DIRECTION === 'inOut' ? len - 1 - i : i;           // outIn: from the outermost LED inwards; inOut: from the innermost outwards
		if (SL_BRAKE_AREA !== 'right') sl_setLeft(frame, slot, colour);
		if (SL_BRAKE_AREA !== 'left') sl_setRight(frame, slot, colour);
	});
	return frame;
}

function sl_brakeLayer() {
	if (!SL_BRAKE_ENABLED || sl_p('IsInPit') == 1) return null;
	const pct = sl_brakePct();
	return SL_BRAKE_AREA === 'top' ? sl_brakeCentreLayer(pct) : sl_brakeSidesLayer(pct);
}

// Pit lane (centre) -------------------------------------------------------------------------

function sl_pitIndicatorLayer() {
	if (!sl_fx('PitIndicator')) return null;
	if (sl_p('IsInPitLane') != 1) return null;
	const mid = Math.max(0, SL_CENTRE - 4);
	const centre = sl_tick(300)
		? [SL_C.pitLane, SL_OFF].concat(Array(mid).fill(SL_OFF), [SL_OFF, SL_C.pitLane])
		: [SL_OFF, SL_C.pitLane].concat(Array(mid).fill(SL_OFF), [SL_C.pitLane, SL_OFF]);
	const frame = sl_blank();
	sl_setCentre(frame, centre);
	return frame;
}

function sl_speedLimiterLayer() {
	if (!sl_fx('PitLimiter')) return null;
	if (sl_p('PitLimiterOn') != 1) return null;
	const t = sl_tick(300);
	const mid = Array.from({ length: Math.max(0, SL_CENTRE - 4) }, (_, i) =>
		(i % 2 === 0) === (t === 0) ? SL_C.pitLimiter1 : SL_C.pitLimiter2);
	// outer 2+2 stay transparent in the pit lane so the pit lane indicator shows through
	const edge = sl_p('IsInPitLane') == 1 ? [null, null] : [SL_OFF, SL_OFF];
	const frame = sl_blank();
	sl_setCentre(frame, edge.concat(mid, edge));
	return frame;
}

function sl_pitEffectsLayer() {
	const inPit = sl_p('IsInPitLane') == 1;
	const limiter = sl_p('PitLimiterOn') == 1;
	const exited = sl_edge('pitExit', sl_p('IsInPitLane'), -1, SL_PIT_EXIT_MS);
	const frame = sl_blank();

	const limit = sl_pitSpeedLimit();
	if (sl_fx('PitSpeeding') && inPit && !limiter && sl_p('SpeedKmh') > limit) {   // speeding: full width
		const t = sl_tick(125);
		sl_setCentre(frame, Array.from({ length: SL_CENTRE }, (_, i) =>
			(i % 2 === 0) === (t === 0) ? SL_C.pitSpeeding : SL_OFF));
		return frame;
	}
	if (!sl_fx('PitAlerts')) return null;
	let colour = null;
	if (inPit && !limiter && sl_p('SpeedKmh') > 1) colour = SL_C.pitEntry;  // no limiter in the pit lane
	else if (exited && !inPit && limiter) colour = SL_C.pitExit;            // just left with limiter on
	if (!colour) return null;
	const mid = Math.max(0, SL_CENTRE - 4);
	sl_setCentre(frame, sl_tick(125)
		? [colour, SL_OFF].concat(Array(mid).fill(null), [SL_OFF, colour])
		: [SL_OFF, colour].concat(Array(mid).fill(null), [colour, SL_OFF]));
	return frame;
}

// ============================== side layers ====================================

function sl_drsLayer() {
	let colour = null;
	if (sl_fx('Drs') && sl_p('DRSEnabled') == 1) colour = SL_C.drsOn;
	else if (sl_fx('Drs') && sl_p('DRSAvailable') == 1) colour = sl_tick(150) ? SL_C.drsOn : SL_OFF;
	else if (sl_fx('DrsDetect') && sl_drsDetectionPoint()) colour = SL_C.drsDetect;
	if (!colour) return null;
	const frame = sl_blank();
	sl_setRange(frame, 'Drs', colour);
	return frame;
}

function sl_kersLayer() {
	if (!sl_fx('ErsLapLimit')) return null;
	const kers = sl_kersLeft();
	if (!(kers > 0)) return null;
	const colour = kers > 50 ? SL_LEVEL.high : kers > 25 ? SL_LEVEL.yellow : kers > 10 ? SL_LEVEL.orange : SL_LEVEL.red;
	const frame = sl_blank();
	sl_setRange(frame, 'ErsLapLimit', colour, true, false);
	return frame;
}

function sl_absTcLayer() {
	const g = sl_game();
	if (sl_isF1() || g === 'BeamNgDrive') return null;
	const frame = sl_blank();
	if (sl_fx('Abs') && sl_p('ABSActive') == 1) sl_setRange(frame, 'Abs', SL_C.abs);
	if (sl_fx('Tc') && (sl_p('TCActive') == 1 || sl_p('DahlDesign.TCActive') == true)) sl_setRange(frame, 'Tc', SL_C.tc);
	return frame;
}

function sl_ersSocLayer() {
	if (!sl_fx('ErsCharge')) return null;
	const soc = sl_ersSoC();
	if (!(soc > 0)) return null;
	let colour;
	if (soc > 95) colour = sl_tick(350) ? SL_LEVEL.high : SL_OFF;
	else if (soc > 50) colour = SL_LEVEL.high;
	else if (soc > 25) colour = SL_LEVEL.yellow;
	else if (soc > 10) colour = SL_LEVEL.orange;
	else if (soc > 5) colour = SL_LEVEL.red;
	else colour = sl_tick(350) ? SL_LEVEL.red : SL_OFF;
	const frame = sl_blank();
	sl_setRange(frame, 'ErsCharge', colour, false, true);
	return frame;
}

function sl_ersEventsLayer() {
	const frame = sl_blank();
	if (SL_SHOW_ERS_REGEN && sl_group('Ers') && sl_ersRegenerating()) sl_setRange(frame, 'ErsDeploy', SL_C.ersRegen, false, true);
	if (sl_fx('ErsDeploy') && sl_ersDeploying()) sl_setRange(frame, 'ErsDeploy', SL_C.ersDeploy, false, true);
	return frame;
}

// ERS deployment mode of an F1 car: 0 none, 1 medium, 2 hotlap, 3 overtake (m_ersDeployMode of the car status packet).
function sl_ersModeLayer() {
	if (!sl_fx('ErsMode') || !sl_isF1()) return null;
	const mode = sl_p('GameRawData.PlayerCarStatusData.m_ersDeployMode');
	const colour = { 1: SL_C.ersMedium, 2: SL_C.ersHotlap, 3: SL_C.ersOvertake }[mode];
	if (!colour) return null;
	const frame = sl_blank();
	sl_setRange(frame, 'ErsMode', colour, true, false);
	return frame;
}

// Under the safety car (F1 games): the delta to the safety car's target time in seconds (m_safetyCarDelta of the lap data).
// Within a fifth of a second the two middle LEDs light in the OK colour; further off, a bar grows from the middle to one side
// (full at 2 seconds), to the right for a positive and to the left for a negative delta. "Swap" turns the direction around.
function sl_scDeltaLayer() {
	if (!sl_fx('ScDelta') || !sl_isF1()) return null;
	if (![1, 2].includes(sl_p('DataCorePlugin.GameRawData.PacketSessionData.m_safetyCarStatus'))) return null;
	let delta = Number(sl_p('GameRawData.PlayerLapData.m_safetyCarDelta'));
	if (!isFinite(delta)) return null;
	if (SL_CFG.FxScDeltaSwap === true) delta = -delta;
	const L = SL_CENTRE, half = Math.floor(L / 2);
	const centre = Array(L).fill(SL_OFF);
	if (Math.abs(delta) <= 0.2) {
		centre[half - 1] = SL_C.scOk;
		centre[half] = SL_C.scOk;
	} else {
		const lit = Math.max(1, Math.round(Math.min(1, Math.abs(delta) / 2) * half));
		for (let i = 0; i < lit; i++) centre[delta > 0 ? half + i : half - 1 - i] = SL_C.scOff;
	}
	const frame = sl_blank();
	sl_setCentre(frame, centre);
	return frame;
}

function sl_p2pLayer() {
	if (!sl_fx('P2p')) return null;
	if (!sl_p('PushToPassActive')) return null;
	const frame = sl_blank();
	sl_setRange(frame, 'P2p', SL_C.p2p, false, true);
	return frame;
}

function sl_turnLayer() {
	if (!sl_fx('Turn')) return null;
	const left = sl_p('TurnIndicatorLeft') == 1;
	const right = sl_p('TurnIndicatorRight') == 1;
	if (!left && !right) return null;
	const steady = ['BeamNgDrive', 'ETS2', 'ATS'].includes(sl_game());
	const colour = steady || (Date.now() % 650) < 250 ? SL_C.indicator : SL_OFF;
	const frame = sl_blank();
	if (left) sl_setRange(frame, 'Turn', colour, true, false);
	if (right) sl_setRange(frame, 'Turn', colour, false, true);
	return frame;
}

function sl_liftCoastLayer() {
	if (!sl_fx('LiftCoast')) return null;
	const value = sl_p('GameRawData.PlayerNativeTelemetry.mLiftAndCoastProgress') ?? 0;
	if (!(value > 0)) return null;
	const frame = sl_blank();
	const step = 254 / sl_fxRange('LiftCoast').len;                       // fills from the inner end of its range outwards
	sl_setRange(frame, 'LiftCoast', (k, len) => value >= (len - 1 - k) * step ? SL_C.liftCoast : null);
	return frame;
}

function sl_lowFuelLayer() {
	if (!sl_fx('LowFuel')) return null;
	if (!sl_pulse('lowFuel', sl_p('CarSettings_FuelAlertActive') == 1, SL_LOWFUEL_MS)) return null;
	if (sl_p('CarSettings_FuelAlertActive') != 1) return null;
	const frame = sl_blank();
	const len = sl_fxRange('LowFuel').len;
	const pos = len - 1 - (Math.floor(Date.now() / 200) % len);        // chase towards the outside
	sl_setRange(frame, 'LowFuel', k => k === pos ? SL_C.lowFuel : SL_OFF);
	return frame;
}

function sl_flasherLayer() {
	if (!sl_fx('HeadlightFlash')) return null;
	if (!sl_flashingHeadlights()) return null;
	const frame = sl_blank();
	sl_setRange(frame, 'HeadlightFlash', sl_tick(150) ? SL_C.flasher : SL_OFF);
	return frame;
}

function sl_spotterActive(side) {
	return sl_p(side === 'left' ? 'SpotterCarLeft' : 'SpotterCarRight') == 1;
}

function sl_spotterLayer() {
	if (!sl_fx('Spotter')) return null;
	const frame = sl_blank();
	let any = false;
	for (const side of ['left', 'right']) {
		if (!sl_spotterActive(side)) continue;
		any = true;
		const distance = sl_p(side === 'left' ? 'SpotterCarLeftDistance' : 'SpotterCarRightDistance');
		const close = sl_game() !== 'IRacing' && distance <= 5;
		const t = sl_tick(close ? 150 : 300);
		// two alternating patterns: all but the innermost LED / all but the outermost LED of the range
		sl_setRange(frame, 'Spotter', (k, len) => (len === 1 ? t === 0 : (t === 0 ? k < len - 1 : k > 0)) ? SL_C.spotter : SL_OFF, side === 'left', side === 'right');
	}
	return any ? frame : null;
}

function sl_sidePitLayer() {
	if (!SL_SIDE_PIT || !sl_group('Pit')) return null;
	let arr = null;
	const limit = sl_pitSpeedLimit();
	if (sl_p('IsInPitLane') == 1 && sl_p('PitLimiterOn') == 0 && sl_p('SpeedKmh') > limit) {
		arr = Array(SL_SIDE).fill(sl_tick(125) ? SL_C.pitSpeeding : SL_OFF);
	} else if (sl_p('PitLimiterOn') == 1) {
		arr = Array(SL_SIDE).fill(sl_tick(300) ? SL_C.pitLimiter1 : SL_C.pitLimiter2);
	}
	if (!arr) return null;
	const frame = sl_blank();
	sl_setSides(frame, arr);
	return frame;
}

// Flags -----------------------------------------------------------------------------------------

function sl_flagPattern() {
	const g = sl_game();
	const K = SL_OFF, Y = SL_FLAG.yellow, W = SL_FLAG.white;

	const red = (g === 'IRacing' && sl_p('GameRawData.Telemetry.SessionFlagsDetails.Isred') == true) ||
		(sl_isF1() && sl_p('DataCorePlugin.GameRawData.RedFlagShown') == true) ||
		(g === 'Automobilista2' && sl_p('DataCorePlugin.GameRawData.mHighestFlagColour') == 5);
	const black = sl_p('Flag_Black') == 1;
	const safetyCar = (sl_isF1() && [1, 2].includes(sl_p('DataCorePlugin.GameRawData.PacketSessionData.m_safetyCarStatus'))) ||
		(g === 'IRacing' && sl_p('GameRawData.Telemetry.SessionFlagsDetails.IscautionWaving') == true) ||
		(g === 'LMU' && (sl_p('GameRawData.rules.mTrackRules.mSafetyCarActive') ?? 0) != 0);

	let yellow = sl_p('Flag_Yellow') == 1 ||
		(g === 'Automobilista2' && sl_p('DataCorePlugin.GameRawData.mHighestFlagColour') == 6) ||
		(g === 'IRacing' && sl_p('GameRawData.Telemetry.SessionFlagsDetails.Isyellow') == true) ||
		(g === 'AssettoCorsaCompetizione' && sl_p('DataCorePlugin.GameRawData.Graphics.FlagsDetails.IsACC_YELLOW_FLAG') == 1);
	for (let sector = 1; sector <= 3 && !yellow; sector++) {
		if (g === 'AssettoCorsaCompetizione') yellow = sl_p('GameRawData.Graphics.globalYellow' + sector) == 1;
		if (g === 'LMU') yellow = sl_p('GameRawData.Data.mSectorFlag0' + sector) == 1;
	}

	const whiteSlowCar = (g === 'IRacing' && sl_p('SessionTypeName') != 'Lone Qualify' && sl_p('IRacingExtraProperties.iRacing_SlowCarAhead') == true) ||
		(g === 'AssettoCorsaCompetizione' && sl_p('GameRawData.Graphics.FlagsDetails.IsACC_WHITE_FLAG') == true) ||
		(g === 'Automobilista2' && sl_p('DataCorePlugin.GameRawData.mHighestFlagColour') == 3);
	const whiteFinalLap = (g === 'IRacing' && sl_p('Flag_White') == 1) ||
		(g === 'AssettoCorsaCompetizione' && sl_p('GameRawData.Graphics.globalWhite') == 1) ||
		(g === 'Automobilista2' && sl_p('DataCorePlugin.GameRawData.mHighestFlagColour') == 4) ||
		(!['IRacing', 'AssettoCorsaCompetizione', 'Automobilista2', 'EAWRC23'].includes(g) && sl_p('RemainingLaps') == 1);
	const whiteSlow = (sl_p('Flag_White') == 1 || whiteSlowCar) && !whiteFinalLap;
	const whiteLast = !whiteSlow && sl_pulse('whiteLast', whiteFinalLap, SL_WHITE_LAST_LAP_MS);

	const orange = sl_p('Flag_Orange') == 1 || (g === 'IRacing' && sl_p('GameRawData.Telemetry.SessionFlagsDetails.Isrepair') == true);
	const blue = sl_p('Flag_Blue') == 1 && !sl_spotterActive('left') && !sl_spotterActive('right');
	const green = sl_p('Flag_Green') == 1;
	const checkered = sl_pulse('checkered', sl_p('Flag_Checkered') == 1, SL_CHECKERED_FLAG_MS);

	if (red) return [[SL_FLAG.red], [K]];
	if (black) return [[SL_FLAG.black, K], [K, SL_FLAG.black]];
	if (safetyCar) return [[Y, K], [K, Y]];
	if (yellow) return [[Y], [K]];
	if (whiteSlow || whiteLast) return [[W], [K]];
	if (orange) return [[SL_FLAG.orange], [K]];
	if (blue) return [[SL_FLAG.blue], [K]];
	if (green) return [[SL_FLAG.green], [K]];
	if (checkered) { const C = SL_FLAG.checkered; return [[C, K], [K, C]]; }
	return null;
}

function sl_flagsLayer() {
	if (!sl_fx('Flags')) return null;
	const pattern = sl_flagPattern();
	if (!pattern) return null;
	const colours = pattern[sl_tick(300)];
	const frame = sl_blank();
	sl_setRange(frame, 'Flags', k => colours[k % colours.length]);
	if (SL_LED_FUNCTION === 'flags' && (SL_CFG.FxFlagsCustom !== true || SL_CFG.FxFlagsCentre !== false)) {   // the top row shows the flag as well (a custom position can switch it off)
		sl_setCentre(frame, Array.from({ length: SL_CENTRE }, (_, i) => colours[i % colours.length]));
	}
	return frame;
}

// ============================== animations =====================================

// Palette sample, t in 0..1 across SL_IDLE_PALETTE.
function sl_pal(t) {
	const n = SL_IDLE_PALETTE.length - 1;
	const x = Math.max(0, Math.min(1, t)) * n;
	const i = Math.min(n - 1, Math.floor(x));
	return sl_mix(SL_IDLE_PALETTE[i], SL_IDLE_PALETTE[i + 1], x - i);
}

function sl_dim(colour, brightness) {
	return sl_mix(SL_OFF, colour, Math.max(0, Math.min(1, brightness)));
}

function sl_idleStyle() {
	return (sl_testActive() ? sl_p('__idleStyle') : null) ?? SL_IDLE_STYLE;
}

function sl_idleLayer() {
	if (!SL_IDLE_ANIMATION || !sl_idleNow()) return null;

	const N = SL_TOTAL;
	const speed = { slow: 1.6, fast: 0.6 }[SL_IDLE_SPEED] ?? 1;
	const now = Date.now();
	const period = 4000 * speed;
	const phase = (now % period) / period;                      // 0..1 per cycle
	const cycle = Math.floor(now / period);
	const tri = phase < 0.5 ? phase * 2 : 2 - phase * 2;        // 0 -> 1 -> 0
	const pos = i => i / (N - 1);                               // 0..1 along the strip
	const fromCentre = i => Math.abs(i - (N - 1) / 2) / ((N - 1) / 2);   // 0 centre .. 1 edge
	// pseudo random per LED, deterministic
	const rnd = (i, k) => ((i * 7919 + k * 104729) * 2654435761 >>> 0) / 4294967296;
	const frame = Array(N).fill(SL_OFF);
	const set = (i, colour, brightness) => { if (brightness > 0.01) frame[i] = sl_dim(colour, brightness); };

	switch (sl_idleStyle()) {
		case 'sweep': {                                          // bar sweeping left to right
			const head = phase * (N + 8) - 4;
			for (let i = 0; i < N; i++) set(i, sl_pal(pos(i)), 1 - Math.abs(i - head) / 4);
			break;
		}
		case 'comet': {                                          // bright head with a fading tail
			const head = phase * (N + 10);
			for (let i = 0; i < N; i++) {
				const behind = head - i;
				if (behind >= 0 && behind < 10) set(i, sl_pal(pos(i)), Math.pow(1 - behind / 10, 2));
			}
			break;
		}
		case 'fillDrain': {                                      // fills left to right, then drains the same way
			const edge = (phase < 0.5 ? phase * 2 : phase * 2 - 1) * N;
			for (let i = 0; i < N; i++) {
				if (phase < 0.5 ? i < edge : i >= edge) set(i, sl_pal(pos(i)), 1);
			}
			break;
		}
		case 'centreOut': {                                      // grows from the centre, shrinks back
			for (let i = 0; i < N; i++) if (fromCentre(i) <= tri) set(i, sl_pal(fromCentre(i)), 1);
			break;
		}
		case 'solid':
			for (let i = 0; i < N; i++) set(i, sl_pal(pos(i)), 1);
			break;
		case 'colourFade': {                                     // whole strip drifts through the palette
			const colour = sl_pal(tri);
			for (let i = 0; i < N; i++) set(i, colour, 1);
			break;
		}
		case 'breathe': {
			const level = (1 - Math.cos(phase * 2 * Math.PI)) / 2;
			for (let i = 0; i < N; i++) set(i, sl_pal(pos(i)), level);
			break;
		}
		case 'bounce': {                                         // one dot bouncing end to end
			const dot = tri * (N - 1);
			for (let i = 0; i < N; i++) set(i, sl_pal(pos(i)), 1 - Math.abs(i - dot) / 2);
			break;
		}
		case 'orbit': {                                          // two dots opposite each other, running around
			const dot = phase * N;
			for (let i = 0; i < N; i++) {
				const d1 = Math.min(Math.abs(i - dot), N - Math.abs(i - dot));
				const d2 = Math.min(Math.abs(i - (dot + N / 2) % N), N - Math.abs(i - (dot + N / 2) % N));
				set(i, sl_pal(pos(i)), 1 - Math.min(d1, d2) / 2.5);
			}
			break;
		}
		case 'theatreChase': {                                   // every third LED, creeping along
			const step = Math.floor(now / (160 * speed));
			for (let i = 0; i < N; i++) if ((i + step) % 3 === 0) set(i, sl_pal(pos(i)), 1);
			break;
		}
		case 'alternate': {                                      // odd / even swap
			const odd = Math.floor(now / (450 * speed)) % 2;
			for (let i = 0; i < N; i++) set(i, sl_pal(i % 2 === odd ? 0.1 : 0.9), 1);
			break;
		}
		case 'wipe': {                                           // one colour wipes over the other
			const edge = (phase < 0.5 ? phase * 2 : phase * 2 - 1) * N;
			const first = phase < 0.5 ? sl_pal(0.1) : sl_pal(0.55);
			const second = phase < 0.5 ? SL_OFF : sl_pal(0.1);
			for (let i = 0; i < N; i++) {
				const colour = i < edge ? first : second;
				if (colour !== SL_OFF) frame[i] = colour;
			}
			break;
		}
		case 'rain': {                                           // random drops that fade out
			for (let i = 0; i < N; i++) {
				const per = (900 + Math.floor(rnd(i, 1) * 7) * 250) * speed;
				const age = (now + rnd(i, 2) * per) % per;
				set(i, sl_pal(rnd(i, 3)), 1 - age / (per * 0.35));
			}
			break;
		}
		case 'ripple': {                                         // ring expanding from a changing spot
			const origin = Math.floor(rnd(cycle, 4) * N);
			const radius = phase * N * 0.7;
			for (let i = 0; i < N; i++) set(i, sl_pal(pos(i)), (1 - Math.abs(Math.abs(i - origin) - radius) / 2.5) * (1 - phase));
			break;
		}
		case 'twinkle': {                                        // sparse random sparkle
			for (let i = 0; i < N; i++) {
				const per = (600 + Math.floor(rnd(i, 5) * 8) * 300) * speed;
				const age = ((now + rnd(i, 6) * per) % per) / per;
				if (age < 0.25) set(i, sl_pal(rnd(i, 7)), Math.pow(Math.sin(age / 0.25 * Math.PI), 2));
			}
			break;
		}
		default: {                                               // 'wave': a ring runs from the centre out to both edges and off, then back in from the edges to the centre
			const width = 0.24;
			const leg = phase < 0.5 ? phase * 2 : (1 - phase) * 2;       // 0 -> 1 -> 0
			const radius = -width + (1 + 2 * width) * leg;               // starts and ends off the strip, so every LED is off between the two runs
			for (let i = 0; i < N; i++) {
				const d = fromCentre(i);
				set(i, sl_pal(d), 1 - Math.abs(d - radius) / width);
			}
		}
	}
	return frame;
}

const sl_engineState = { old: null, start: null };
function sl_engineStartLayer() {
	const started = sl_p('EngineStarted');
	if (sl_engineState.old !== null && started > sl_engineState.old) sl_engineState.start = Date.now();
	sl_engineState.old = started ?? 0;
	if (!sl_fx('EngineStart')) { sl_engineState.start = null; return null; }

	if (sl_engineState.start === null) return null;
	const elapsed = Date.now() - sl_engineState.start;
	if (elapsed < 0 || elapsed >= SL_ENGINE_START_MS) { sl_engineState.start = null; return null; }

	const p1 = SL_ENGINE_START_MS * 0.4, hold = SL_ENGINE_START_MS * 0.2;
	const frame = Array(SL_TOTAL).fill(SL_OFF);
	if (elapsed < p1) {                                                   // spark shower
		const progress = elapsed / p1;
		const sparks = 1 + Math.floor(progress * progress * (SL_TOTAL * 0.7 - 1));
		const colour = sl_mix(SL_C.white, SL_C.engineStart, progress);
		for (let i = 0; i < sparks; i++) frame[Math.floor(Math.random() * SL_TOTAL)] = colour;
	} else if (elapsed < p1 + hold) {                                     // hold
		frame.fill(SL_C.engineStart);
	} else {                                                              // wipe off from the centre
		const progress = (elapsed - p1 - hold) / (SL_ENGINE_START_MS * 0.4);
		const centre = (SL_TOTAL - 1) / 2, fadeWidth = 2.6;
		const edge = progress * (SL_TOTAL / 2 + fadeWidth);
		for (let i = 0; i < SL_TOTAL; i++) {
			const d = Math.abs(i - centre);
			const on = d < edge - fadeWidth ? 0 : d < edge ? 1 - (edge - d) / fadeWidth : 1;
			frame[i] = sl_mix(SL_OFF, SL_C.engineStart, Math.max(0, on));
		}
	}
	return frame;
}

// ============================== entry point ====================================

// The game is paused or showing a menu (GamePaused is a SimHub property; GameInMenu exists for some games).
function sl_inMenu() {
	return sl_p('DataCorePlugin.GamePaused') == 1 || sl_p('DataCorePlugin.GameInMenu') == 1;
}

// True while the car is switched off or standing in the garage / menu: the game reports RPM 0 and no speed. A missing value does not count.
function sl_carOff() {
	if (sl_p('EngineIgnitionOn') == 0) return true;
	const rpm = sl_p('Rpms'), speed = sl_p('SpeedKmh');
	// Before the first drive of a session the game sends no car data at all while it reports itself running (garage, menu).
	if (sl_p('DataCorePlugin.GameRunning') == 1 && typeof rpm !== 'number') return true;
	return typeof rpm === 'number' && typeof speed === 'number' && rpm <= 0 && speed <= 1;
}

function sl_idleNow() {
	return sl_p('DataCorePlugin.GameRunning') == 0 || sl_inMenu() || sl_carOff();
}

function sl_active() {
	return !sl_idleNow() && sl_p('EngineIgnitionOn') != 0;
}

// The effect layers in the standard order, lowest priority first. The plugin lets the user change the order (setting LayerOrder);
// the fuel level is not part of this list because it always has the lowest priority (see sl_fuelOverlay).
// Standard order: what you would most regret missing wins. Flags are above the spotter, the spotter above the rest.
const SL_LAYER_DEFS = [
	{ id: 'rpm', label: 'RPM and centre display', fn: sl_centreLayer },
	{ id: 'kers', label: 'KERS lap limit', fn: sl_kersLayer },
	{ id: 'drs', label: 'DRS', fn: sl_drsLayer },
	{ id: 'ersCharge', label: 'ERS state of charge', fn: sl_ersSocLayer },
	{ id: 'ersEvents', label: 'ERS deploy and regen', fn: sl_ersEventsLayer },
	{ id: 'ersMode', label: 'ERS mode', fn: sl_ersModeLayer },
	{ id: 'turn', label: 'Turn indicators', fn: sl_turnLayer },
	{ id: 'liftCoast', label: 'Lift and coast', fn: sl_liftCoastLayer },
	{ id: 'lowFuel', label: 'Low fuel warning', fn: sl_lowFuelLayer },
	{ id: 'clutch', label: 'Clutch', fn: sl_clutchLayer },
	{ id: 'pitLane', label: 'Pit lane indicator', fn: sl_pitIndicatorLayer },
	{ id: 'limiter', label: 'Pit limiter', fn: sl_speedLimiterLayer },
	{ id: 'p2p', label: 'Push to pass', fn: sl_p2pLayer },
	{ id: 'brake', label: 'Brake bar', fn: sl_brakeLayer },
	{ id: 'absTc', label: 'ABS and traction control', fn: sl_absTcLayer },       // stay visible while braking
	{ id: 'flasher', label: 'Headlight flash', fn: sl_flasherLayer },
	{ id: 'pitAlerts', label: 'Pit entry and exit alerts', fn: sl_pitEffectsLayer },
	{ id: 'scDelta', label: 'Safety car delta', fn: sl_scDeltaLayer },
	{ id: 'sidePit', label: 'Pit speeding warning', fn: sl_sidePitLayer },
	{ id: 'spotter', label: 'Spotter', fn: sl_spotterLayer },
	{ id: 'flags', label: 'Flags', fn: sl_flagsLayer }
];

// Layer functions in the order given by the setting (ids lowest first). Unknown and double ids are ignored; layers missing from
// the list are put back right above the layer that precedes them in the standard order.
function sl_layerOrder(setting, effectsOn) {
	const known = SL_LAYER_DEFS.map(d => d.id);
	const order = [];
	for (const id of typeof setting === 'string' ? setting.split(',').map(x => x.trim()) : []) {
		if (known.includes(id) && !order.includes(id)) order.push(id);
	}
	known.forEach((id, i) => {
		if (order.includes(id)) return;
		let at = 0;
		for (let j = i - 1; j >= 0; j--) {
			const pos = order.indexOf(known[j]);
			if (pos >= 0) { at = pos + 1; break; }
		}
		order.splice(at, 0, id);
	});
	return order.filter(id => effectsOn !== false || id === 'rpm' || id === 'brake').map(id => SL_LAYER_DEFS.find(d => d.id === id).fn);
}

// Scales the brightness of every lit LED (factor 0..1).
function sl_scale(frame, factor) {
	if (!frame || factor >= 1) return frame;
	for (let i = 0; i < frame.length; i++) {
		if (frame[i] != null && frame[i] !== SL_OFF) frame[i] = sl_mix(SL_OFF, frame[i], factor);
	}
	return frame;
}

function sl_frame() {
	sl_loadConfig();
	const frame = sl_blank();
	sl_paint(frame, sl_scale(sl_safe(sl_idleLayer), SL_BRIGHT_IDLE));
	if (sl_active()) {
		for (const layer of SL_LAYERS) sl_paint(frame, sl_safe(layer));
		sl_safe(() => sl_fuelOverlay(frame));
	}
	sl_paint(frame, sl_safe(sl_engineStartLayer));
	if (sl_testActive() && sl_p('__allOn')) for (let i = 0; i < SL_TOTAL; i++) frame[i] = '#FFFFFFFF';        // brightness test: every LED white
	sl_scale(frame, SL_BRIGHT_GENERAL);
	while (frame.length < SL_MAX_TOTAL) frame.push(null);        // unused LEDs stay transparent
	return frame;
}
