// WebGL bridge for the ray-traced audio (C# side: Scripts/Audio/VAudioNativeWeb.cs and WebAudioBridge.cs).
//
// 1. Vercidium Audio: the page template (Assets/WebGLTemplates/MaidCafe/index.html) loads the SDK's WebAssembly build
//    into window.MaidCafeVAudio = { status: 0 loading | 1 ready | -1 failed, va, threads, message }. The functions below
//    expose it to C# with integer handles (never 0) in place of the native library's pointers, and map JS exceptions to
//    the native result codes the game already checks (0 success, 5 in use, 20 not found, 24/25 true/false).
// 2. Web Audio: browsers ignore Unity's AudioLowPassFilter and AudioReverbZone, so the traced muffling and room reverb
//    are rebuilt here. Every Unity channel is routed through its own low-pass filter (cutoff matched by position to the
//    RaytracedSource that plays it) and 3D channels also feed one shared convolution reverb driven by the traced EAX.

var VAudioWebLib = {
    $VAW__deps: ['$WEBAudio'],
    $VAW: {
        objs: [null],
        warned: {},

        host: function () {
            return (typeof window !== 'undefined' && window.MaidCafeVAudio) ? window.MaidCafeVAudio : null;
        },

        va: function () {
            var h = VAW.host();
            return h && h.status === 1 ? h.va : null;
        },

        add: function (kind, obj) {
            VAW.objs.push({ kind: kind, obj: obj, emitters: 0 });
            return VAW.objs.length - 1;
        },

        entry: function (h) {
            return (h > 0 && h < VAW.objs.length) ? VAW.objs[h] : null;
        },

        obj: function (h) {
            var e = VAW.entry(h);
            return e ? e.obj : null;
        },

        warn: function (where, e) {
            if (VAW.warned[where]) return;
            VAW.warned[where] = true;
            console.warn('[VAudioWeb] ' + where + ': ' + (e && e.message ? e.message : e));
        },

        // Runs fn and returns a native result code.
        run: function (where, fn) {
            try {
                fn();
                return 0;
            } catch (e) {
                var msg = String(e && e.message ? e.message : e);
                if (/in use|still|pending|removed|added to/i.test(msg)) return 5;
                VAW.warn(where, e);
                return 1;
            }
        },

        readMatrix: function (ptr) {
            var b = ptr >> 2;
            var f = HEAPF32;
            return {
                m11: f[b], m12: f[b + 1], m13: f[b + 2], m14: f[b + 3],
                m21: f[b + 4], m22: f[b + 5], m23: f[b + 6], m24: f[b + 7],
                m31: f[b + 8], m32: f[b + 9], m33: f[b + 10], m34: f[b + 11],
                m41: f[b + 12], m42: f[b + 13], m43: f[b + 14], m44: f[b + 15]
            };
        },

        // ------------------------------------------------------------------ Web Audio

        audio: null,

        initAudio: function () {
            if (VAW.audio) return VAW.audio;
            if (typeof WEBAudio === 'undefined' || !WEBAudio.audioContext) return null;
            var ctx = WEBAudio.audioContext;
            var a = { ctx: ctx, channels: new Set(), frame: 1, active: 0, lastDecay: 0.9, lastRatio: 0.7, lastRegen: -1 };

            // Shared reverb: send -> pre-delay -> HF damping -> two convolvers (cross-faded on impulse changes) -> wet.
            a.send = ctx.createGain();
            a.preDelay = ctx.createDelay(1.0);
            a.damp = ctx.createBiquadFilter();
            a.damp.type = 'lowpass';
            a.damp.frequency.value = 8000;
            a.conv = [ctx.createConvolver(), ctx.createConvolver()];
            a.convGain = [ctx.createGain(), ctx.createGain()];
            a.wet = ctx.createGain();
            a.wet.gain.value = 0.15;
            a.send.connect(a.preDelay);
            a.preDelay.connect(a.damp);
            for (var i = 0; i < 2; i++) {
                a.damp.connect(a.conv[i]);
                a.conv[i].connect(a.convGain[i]);
                a.convGain[i].connect(a.wet);
            }
            a.convGain[1].gain.value = 0;
            a.conv[0].buffer = VAW.impulse(ctx, a.lastDecay, a.lastRatio);
            a.wet.connect(ctx.destination);

            // Unity connects each channel's output gain straight to the destination (Audio.js configurePanningNodes);
            // tagged channels are sent to their own filter instead.
            var origConnect = AudioNode.prototype.connect;
            AudioNode.prototype.connect = function (dest) {
                var ch = this.__vawChannel;
                if (ch && dest === ctx.destination && ch.__vawFilter) {
                    origConnect.call(this, ch.__vawFilter);
                    return dest;
                }
                return origConnect.apply(this, arguments);
            };

            VAW.audio = a;
            return a;
        },

        tagChannel: function (a, ch) {
            var ctx = a.ctx;
            ch.__vawFilter = ctx.createBiquadFilter();
            ch.__vawFilter.type = 'lowpass';
            ch.__vawFilter.frequency.value = 22000;
            ch.__vawFilter.Q.value = 0.707;
            ch.__vawSend = ctx.createGain();
            ch.__vawSend.gain.value = 0;
            ch.__vawCutoff = 22000;
            ch.__vawSendLevel = 0;
            ch.__vawFilter.connect(ctx.destination);
            ch.__vawFilter.connect(ch.__vawSend);
            ch.__vawSend.connect(a.send);
            ch.gain.__vawChannel = ch;
            a.channels.add(ch);
            if (ch.source) {
                try { ch.configurePanningNodes(); } catch (e) { VAW.warn('configurePanningNodes', e); }
            }
        },

        untagChannel: function (a, ch) {
            try {
                ch.__vawFilter.disconnect();
                ch.__vawSend.disconnect();
            } catch (e) { }
            a.channels.delete(ch);
        },

        // Exponentially decaying stereo noise; the band above ~1.5 kHz decays decay*hfRatio seconds to -60 dB.
        impulse: function (ctx, decay, hfRatio) {
            var rate = ctx.sampleRate;
            decay = Math.max(0.1, Math.min(decay, 8));
            hfRatio = Math.max(0.1, Math.min(hfRatio, 2));
            var len = Math.max(64, Math.floor(rate * Math.min(decay * 1.1 + 0.05, 6)));
            var buf = ctx.createBuffer(2, len, rate);
            var stepLF = Math.exp(-6.9078 / (decay * rate));
            var stepHF = Math.exp(-6.9078 / (decay * hfRatio * rate));
            var a1 = Math.exp(-2 * Math.PI * 1500 / rate);
            var fade = Math.floor(rate * 0.004);
            for (var c = 0; c < 2; c++) {
                var d = buf.getChannelData(c);
                var lp = 0, envLF = 1, envHF = 1;
                for (var i = 0; i < len; i++) {
                    var n = Math.random() * 2 - 1;
                    lp = lp * a1 + n * (1 - a1);
                    var v = lp * envLF + (n - lp) * envHF;
                    d[i] = i < fade ? v * (i / fade) : v;
                    envLF *= stepLF;
                    envHF *= stepHF;
                }
            }
            return buf;
        },

        lin: function (mb) {
            return Math.pow(10, mb / 2000);
        },

        // Same parameters as Unity's AudioReverbZone (millibels, seconds, Hz).
        applyReverb: function (a, room, roomHF, decay, hfRatio, reflections, reflDelay, late, lateDelay, hfRef) {
            var ctx = a.ctx;
            var t = ctx.currentTime;
            var wet = VAW.lin(room) * Math.max(VAW.lin(late), 0.5 * VAW.lin(reflections)) * 0.55;
            a.wet.gain.setTargetAtTime(Math.max(0, Math.min(wet, 1.5)), t, 0.08);
            a.preDelay.delayTime.setTargetAtTime(Math.max(0, Math.min(reflDelay + lateDelay, 0.5)), t, 0.08);
            // Second-order low-pass whose attenuation at hfRef equals roomHF.
            var db = -roomHF / 100;
            var cut = db < 0.05 ? 20000 : hfRef / Math.pow(Math.pow(10, db / 10) - 1, 0.25);
            a.damp.frequency.setTargetAtTime(Math.max(300, Math.min(cut, 20000)), t, 0.08);

            var changed = Math.abs(decay - a.lastDecay) / a.lastDecay > 0.15 || Math.abs(hfRatio - a.lastRatio) > 0.15;
            if (changed && t - a.lastRegen > 0.5) {
                var next = 1 - a.active;
                a.conv[next].buffer = VAW.impulse(ctx, decay, hfRatio);
                a.convGain[a.active].gain.setValueAtTime(a.convGain[a.active].gain.value, t);
                a.convGain[a.active].gain.linearRampToValueAtTime(0, t + 0.3);
                a.convGain[next].gain.setValueAtTime(0, t);
                a.convGain[next].gain.linearRampToValueAtTime(1, t + 0.3);
                a.active = next;
                a.lastDecay = decay;
                a.lastRatio = hfRatio;
                a.lastRegen = t;
            }
        }
    },

    // ---------------------------------------------------------------------- status

    VAW_Status: function () {
        var h = VAW.host();
        return h ? h.status : -1;
    },

    VAW_StatusMessage: function () {
        var h = VAW.host();
        var s = h ? String(h.message || '') : 'page template did not load the SDK (window.MaidCafeVAudio missing)';
        var size = lengthBytesUTF8(s) + 1;
        var buffer = _malloc(size);
        stringToUTF8(s, buffer, size);
        return buffer;
    },

    VAW_ThreadCount: function () {
        var h = VAW.host();
        return h && h.threads ? h.threads : 1;
    },

    // ---------------------------------------------------------------------- world

    VAW_WorldCreate: function () {
        var va = VAW.va();
        if (!va) return 0;
        try {
            return VAW.add('world', va.World_Create());
        } catch (e) {
            VAW.warn('World_Create', e);
            return 0;
        }
    },

    VAW_WorldDestroy: function (h) {
        var w = VAW.obj(h);
        if (!w) return 18;
        var r = VAW.run('World.dispose', function () { if (w.dispose) w.dispose(); else w.destroy(); });
        if (r === 0) VAW.objs[h] = null;
        return r;
    },

    VAW_WorldUpdate: function (h) {
        var w = VAW.obj(h);
        if (!w) return 18;
        try {
            var running = w.areThreadsRunning;
            w.update();
            return running ? 21 : 0;
        } catch (e) {
            VAW.warn('World.update', e);
            return 1;
        }
    },

    VAW_WorldThreadsRunning: function (h) {
        var w = VAW.obj(h);
        try { return w && w.areThreadsRunning ? 1 : 0; } catch (e) { return 0; }
    },

    VAW_WorldSetVector: function (h, which, x, y, z) {
        var w = VAW.obj(h);
        if (!w) return 18;
        var v = { x: x, y: y, z: z };
        return VAW.run('World.setVector' + which, function () {
            if (which === 0) w.position = v;
            else if (which === 1) w.size = v;
            else w.cameraPosition = v;
        });
    },

    VAW_WorldSetFloat: function (h, which, v) {
        var w = VAW.obj(h);
        if (!w) return 18;
        return VAW.run('World.setFloat' + which, function () {
            if (which === 0) w.metersPerUnit = v;
            else if (which === 1) w.cameraPitch = v;
            else w.cameraYaw = v;
        });
    },

    VAW_WorldSetInt: function (h, which, v) {
        var w = VAW.obj(h);
        if (!w) return 18;
        if (which === 3) return 1; // debug window rendering: dev build only, never available in a browser
        return VAW.run('World.setInt' + which, function () {
            if (which === 0) {
                var threads = VAW.host() && VAW.host().threads ? VAW.host().threads : 1;
                w.maximumConcurrencyLevel = Math.max(1, Math.min(v, threads));
            }
            else if (which === 1) w.coordinateSystem = v;
            else if (which === 2) w.emittersOutsideTheWorldAreMuffled = v !== 0;
            else if (which === 4) w.manualCamera = v !== 0;
            else if (which === 5) w.logMemoryAllocationWarnings = v !== 0;
        });
    },

    VAW_WorldGetDouble: function (h, which) {
        var w = VAW.obj(h);
        if (!w) return 0;
        try {
            if (which === 0) return w.raytracingTime;
            if (which === 1) return w.preparationTime;
            return w.mainThreadTime;
        } catch (e) {
            return 0;
        }
    },

    VAW_WorldEmitterCount: function (h) {
        var e = VAW.entry(h);
        return e ? e.emitters : 0;
    },

    VAW_WorldAddPrimitive: function (h, p) {
        var w = VAW.obj(h), prim = VAW.obj(p);
        if (!w || !prim) return 18;
        return VAW.run('World.addPrimitive', function () { w.addPrimitive(prim); });
    },

    VAW_WorldRemovePrimitive: function (h, p) {
        var w = VAW.obj(h), prim = VAW.obj(p);
        if (!w || !prim) return 18;
        return VAW.run('World.removePrimitive', function () { w.removePrimitive(prim); });
    },

    VAW_WorldAddEmitter: function (h, e) {
        var we = VAW.entry(h), em = VAW.obj(e);
        if (!we || !we.obj || !em) return 18;
        var r = VAW.run('World.addEmitter', function () { we.obj.addEmitter(em); });
        if (r === 0) we.emitters++;
        return r;
    },

    VAW_WorldRemoveEmitter: function (h, e) {
        var we = VAW.entry(h), em = VAW.obj(e);
        if (!we || !we.obj || !em) return 18;
        var r = VAW.run('World.removeEmitter', function () { we.obj.removeEmitter(em); });
        if (r === 0 && we.emitters > 0) we.emitters--;
        return r;
    },

    VAW_WorldCreateMaterial: function (h, id) {
        var w = VAW.obj(h), va = VAW.va();
        if (!w || !va) return 18;
        return VAW.run('World.addMaterial', function () { w.addMaterial(id, va.MaterialProperties_Create()); });
    },

    VAW_WorldSetMaterialFloat: function (h, id, which, v) {
        var w = VAW.obj(h);
        if (!w) return 18;
        return VAW.run('Material.set' + which, function () {
            var m = w.getMaterial(id);
            if (which === 0) m.absorptionLF = v;
            else if (which === 1) m.absorptionHF = v;
            else if (which === 2) m.scattering = v;
            else if (which === 3) m.transmissionLF = v;
            else m.transmissionHF = v;
        });
    },

    // ---------------------------------------------------------------------- emitter

    VAW_EmitterCreate: function () {
        var va = VAW.va();
        if (!va) return 0;
        try {
            return VAW.add('emitter', va.Emitter_Create());
        } catch (e) {
            VAW.warn('Emitter_Create', e);
            return 0;
        }
    },

    VAW_EmitterDestroy: function (h) {
        var e = VAW.obj(h);
        if (!e) return 18;
        var r = VAW.run('Emitter.destroy', function () { e.destroy(); });
        if (r === 0) VAW.objs[h] = null;
        return r;
    },

    // op: 0 add target, 1 remove target, 2 has raytraced target (24 true / 25 false).
    VAW_EmitterTarget: function (h, t, op) {
        var e = VAW.obj(h), target = VAW.obj(t);
        if (!e || !target) return 18;
        if (op === 2) {
            try { return e.hasRaytracedTarget(target) ? 24 : 25; } catch (x) { return 25; }
        }
        return VAW.run(op === 0 ? 'Emitter.addTarget' : 'Emitter.removeTarget', function () {
            if (op === 0) e.addTarget(target); else e.removeTarget(target);
        });
    },

    VAW_EmitterGetTargetFilter: function (h, t, outPtr) {
        var e = VAW.obj(h), target = VAW.obj(t);
        if (!e || !target) return 18;
        try {
            if (!e.hasRaytracedTarget(target)) return 20;
            var f = e.getTargetFilter(target);
            HEAPF32[outPtr >> 2] = f.gainLF;
            HEAPF32[(outPtr >> 2) + 1] = f.gainHF;
            return 0;
        } catch (x) {
            VAW.warn('Emitter.getTargetFilter', x);
            return 20;
        }
    },

    VAW_EmitterSetPosition: function (h, x, y, z) {
        var e = VAW.obj(h);
        if (!e) return 18;
        return VAW.run('Emitter.setPosition', function () { e.setPosition(x, y, z); });
    },

    VAW_EmitterSetInt: function (h, which, v) {
        var e = VAW.obj(h);
        if (!e) return 18;
        var names = ['reverbRayCount', 'reverbBounceCount', 'occlusionRayCount', 'occlusionBounceCount',
            'permeationRayCount', 'permeationBounceCount', 'ambientOcclusionRayCount', 'ambientOcclusionBounceCount',
            'ambientPermeationRayCount', 'ambientPermeationBounceCount', 'maxEchogramTime', 'echogramGranularity',
            'clampPosition', 'affectsGroupedEAX'];
        var name = names[which];
        if (!name) return 1;
        return VAW.run('Emitter.' + name, function () { e[name] = which >= 12 ? v !== 0 : v; });
    },

    VAW_EmitterSetFloat: function (h, which, v) {
        var e = VAW.obj(h);
        if (!e) return 18;
        return VAW.run('Emitter.reverbEnergyCap', function () { e.reverbEnergyCap = v; });
    },

    VAW_EmitterSetName: function (h, namePtr) {
        var e = VAW.obj(h);
        if (!e) return 18;
        var name = UTF8ToString(namePtr);
        return VAW.run('Emitter.name', function () { e.name = name; });
    },

    // Writes 26 floats (processed reverb + EAX, order documented in VAudioNativeWeb.cs); 0 until reverb was traced.
    VAW_EmitterGetEAX: function (h, outPtr) {
        var e = VAW.obj(h);
        if (!e) return 0;
        try {
            if (e.initialising) return 0;
            var p = e.processedReverb, x = e.eax, b = outPtr >> 2, f = HEAPF32;
            f[b] = p.outsidePercent; f[b + 1] = p.returnedPercent; f[b + 2] = p.materialAbsorptionLF;
            f[b + 3] = p.materialAbsorptionHF; f[b + 4] = p.materialRoughness;
            f[b + 5] = x.reflectionsDelay; f[b + 6] = x.density; f[b + 7] = x.diffusion; f[b + 8] = x.gainLF;
            f[b + 9] = x.gainHF; f[b + 10] = x.gain; f[b + 11] = x.decayTime; f[b + 12] = x.decayLFRatio;
            f[b + 13] = x.decayHFRatio; f[b + 14] = x.reflectionsGain; f[b + 15] = x.lateReverbGain;
            f[b + 16] = x.lateReverbDelay; f[b + 17] = x.echoTime; f[b + 18] = x.echoDepth; f[b + 19] = x.modulationTime;
            f[b + 20] = x.modulationDepth; f[b + 21] = x.airAbsorptionGainHF; f[b + 22] = x.hfReference;
            f[b + 23] = x.lfReference; f[b + 24] = x.roomRolloffFactor; f[b + 25] = x.decayHFLimit ? 1 : 0;
            return 1;
        } catch (err) {
            VAW.warn('Emitter.eax', err);
            return 0;
        }
    },

    // ---------------------------------------------------------------------- primitives

    VAW_VoxelCreate: function (width, height, depth) {
        var va = VAW.va();
        if (!va) return 0;
        try {
            return VAW.add('voxel', va.VoxelPrimitive_Create(width, height, depth));
        } catch (e) {
            VAW.warn('VoxelPrimitive_Create', e);
            return 0;
        }
    },

    // data: width*height*depth ints in the WebAssembly layout x + y*width + z*width*height.
    VAW_VoxelSetData: function (h, dataPtr, count) {
        var v = VAW.obj(h);
        if (!v) return 18;
        var view = HEAP32.subarray(dataPtr >> 2, (dataPtr >> 2) + count);
        return VAW.run('VoxelPrimitive.setData', function () { v.setData(view); });
    },

    VAW_PrismCreate: function () {
        var va = VAW.va();
        if (!va) return 0;
        try {
            return VAW.add('prism', va.PrismPrimitive_Create());
        } catch (e) {
            VAW.warn('PrismPrimitive_Create', e);
            return 0;
        }
    },

    VAW_PrismSetSize: function (h, x, y, z) {
        var p = VAW.obj(h);
        if (!p) return 18;
        return VAW.run('PrismPrimitive.size', function () { p.size = { x: x, y: y, z: z }; });
    },

    VAW_PrimitiveSetMaterial: function (h, material) {
        var p = VAW.obj(h);
        if (!p) return 18;
        return VAW.run('Primitive.material', function () { p.material = material; });
    },

    VAW_PrimitiveSetTransform: function (h, matrixPtr) {
        var p = VAW.obj(h);
        if (!p) return 18;
        var m = VAW.readMatrix(matrixPtr);
        return VAW.run('Primitive.transform', function () { p.transform = m; });
    },

    VAW_PrimitiveDestroy: function (h) {
        var p = VAW.obj(h);
        if (!p) return 18;
        var r = VAW.run('Primitive.destroy', function () { p.destroy(); });
        if (r === 0) VAW.objs[h] = null;
        return r;
    },

    // ---------------------------------------------------------------------- Web Audio sync (once per frame)

    // sources: count x (x, y, z, cutoff Hz) of the playing traced sources; reverb: 9 floats (room, roomHF, decayTime,
    // decayHFRatio, reflections, reflectionsDelay, reverb, reverbDelay, HFReference) or 0 to leave the reverb as is.
    VAW_AudioSync: function (sourcesPtr, count, reverbPtr) {
        var a = VAW.initAudio();
        if (!a) return;
        var f = HEAPF32;
        var base = sourcesPtr >> 2;
        var t = a.ctx.currentTime;
        var inst = WEBAudio.audioInstances;
        var frame = ++a.frame;
        var matched = 0, spatialCount = 0, minCutoff = 22000;
        for (var key in inst) {
            var ch = inst[key];
            if (!ch || !ch.gain || !ch.panner || typeof ch.configurePanningNodes !== 'function') continue;
            if (!ch.__vawFilter) VAW.tagChannel(a, ch);
            ch.__vawSeen = frame;
            var spatial = ch.spatialBlendLevel > 0;
            var cutoff = 22000;
            if (spatial && ch.source) {
                spatialCount++;
                var p = ch.panner;
                var x = p.positionX ? p.positionX.value : (p._x || 0);
                var y = p.positionY ? p.positionY.value : (p._y || 0);
                var z = p.positionZ ? p.positionZ.value : (p._z || 0);
                var best = 0.25; // within 0.5 m: sources that close share the same occlusion anyway
                for (var i = 0; i < count; i++) {
                    var b = base + i * 4;
                    var dx = f[b] - x, dy = f[b + 1] - y, dz1 = f[b + 2] - z, dz2 = f[b + 2] + z;
                    var d = dx * dx + dy * dy + Math.min(dz1 * dz1, dz2 * dz2);
                    if (d < best) {
                        best = d;
                        cutoff = f[b + 3];
                    }
                }
                if (best < 0.25) {
                    matched++;
                    if (cutoff < minCutoff) minCutoff = cutoff;
                }
            }
            if (Math.abs(cutoff - ch.__vawCutoff) > ch.__vawCutoff * 0.01) {
                ch.__vawFilter.frequency.setTargetAtTime(cutoff, t, 0.03);
                ch.__vawCutoff = cutoff;
            }
            var send = spatial ? 1 : 0;
            if (send !== ch.__vawSendLevel) {
                ch.__vawSend.gain.setTargetAtTime(send, t, 0.02);
                ch.__vawSendLevel = send;
            }
        }
        a.channels.forEach(function (c) {
            if (c.__vawSeen !== frame) VAW.untagChannel(a, c);
        });
        if (reverbPtr) {
            var r = reverbPtr >> 2;
            VAW.applyReverb(a, f[r], f[r + 1], f[r + 2], f[r + 3], f[r + 4], f[r + 5], f[r + 6], f[r + 7], f[r + 8]);
        }
        if (typeof window !== 'undefined' && window.MaidCafeVAudioDebug && frame % 300 === 0) {
            console.log('[VAudioWeb] channels ' + a.channels.size + ', playing 3D ' + spatialCount + ', matched ' + matched +
                ', traced sources ' + count + ', lowest cutoff ' + Math.round(minCutoff) + ' Hz, reverb wet ' + a.wet.gain.value.toFixed(3) + ', decay ' + a.lastDecay.toFixed(2));
        }
    }
};

autoAddDeps(VAudioWebLib, '$VAW');
mergeInto(LibraryManager.library, VAudioWebLib);
