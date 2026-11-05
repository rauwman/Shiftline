// Shiftline loader profile script. It contains no LED logic: it runs the script that the Shiftline plugin publishes as the
// property Shiftline.ScriptSource, and compiles it again whenever Shiftline.ScriptBuild changes. A plugin update therefore
// updates the LEDs without importing the profile again.
// Without the plugin (or when the script does not compile) LED 1 and 2 blink red in turns.

function sl_notice() {
	const frame = [];
	for (let i = 0; i < 48; i++) frame.push(null);
	const on = Math.floor(Date.now() / 500) % 2 === 0;
	frame[0] = on ? '#FFFF0000' : '#FF000000';
	frame[1] = on ? '#FF000000' : '#FFFF0000';
	return frame;
}

// Where the compiled script is kept between frames: the formula's root object when there is one, otherwise the global object.
function sl_holder() {
	try {
		if (typeof root !== 'undefined' && root) return root;
	} catch (e) { }
	return (function () { return this; })();
}

function sl_loader() {
	const build = $prop('Shiftline.ScriptBuild');
	const source = $prop('Shiftline.ScriptSource');
	if (typeof source !== 'string' || source.length < 1000) return sl_notice();
	const holder = sl_holder();
	if (!holder) return sl_notice();
	if (holder.__shiftlineBuild !== build || typeof holder.__shiftlineFrame !== 'function') {
		try {
			holder.__shiftlineFrame = new Function(source + '\nreturn sl_frame;')();
			holder.__shiftlineBuild = build;
		} catch (e) {
			return sl_notice();
		}
	}
	return holder.__shiftlineFrame();
}
