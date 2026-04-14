window.SceneSharedClient = (() => {
    function createToaster({ wrap, toastClass, titleClass, metaClass, dedupeMs = 1800, lifetimeMs = 2600 }) {
        const lastToastAt = new Map();
        return (title, meta) => {
            if (!wrap) return;
            const key = `${title}:${meta}`;
            const now = Date.now();
            const last = lastToastAt.get(key) || 0;
            if (now - last < dedupeMs) return;
            lastToastAt.set(key, now);

            const el = document.createElement('div');
            el.className = toastClass;
            el.innerHTML = `<div class="${titleClass}">${title}</div><div class="${metaClass}">${meta}</div>`;
            wrap.appendChild(el);
            setTimeout(() => {
                el.style.opacity = '0';
                el.style.transform = 'translateY(8px)';
                setTimeout(() => el.remove(), 220);
            }, lifetimeMs);
        };
    }

    function setStatusDot(el, isOn, onClass, offClass) {
        if (!el) return;
        el.classList.toggle(onClass, !!isOn);
        el.classList.toggle(offClass, !isOn);
    }

    function buildSceneOptions(inputs) {
        return {
            includeHuman: !!inputs.includeHuman?.checked,
            includeAnimal: !!inputs.includeAnimal?.checked,
            includeObject: !!inputs.includeObject?.checked,
            processAllModels: !!inputs.processAll?.checked,
            enableSsdModel: !!inputs.ssd?.checked,
            enableFaceCascade: !!inputs.face?.checked,
            enableFullBodyCascade: !!inputs.fullBody?.checked,
            enableCatCascade: !!inputs.cat?.checked,
            sceneModelBackend: String(inputs.sceneModelBackend?.value || 'yolo').toLowerCase(),
            ssdConfidenceThreshold: Number(inputs.ssdConfidenceThreshold?.value ?? 0.40),
            yoloInputSize: parseInt(inputs.yoloInputSize?.value ?? '640', 10) || 640,
            yoloNmsThreshold: Number(inputs.yoloNmsThreshold?.value ?? 0.45),
            fullBodyMinNeighbors: parseInt(inputs.fullBodyMinNeighbors?.value ?? '3', 10) || 3,
            catMinNeighbors: parseInt(inputs.catMinNeighbors?.value ?? '6', 10) || 6,
            catMinAreaRatio: Number(inputs.catMinAreaRatio?.value ?? 0.004)
        };
    }

    function bindProcessAll(master, slaves) {
        if (!master) return;
        master.addEventListener('change', () => {
            if (!master.checked) return;
            slaves.forEach(el => {
                if (el) el.checked = true;
            });
        });
    }

    function createFrameProcessor({
        videoElement,
        getConstraints,
        getTargetFps,
        onFrame,
        onDroppedFrame
    }) {
        let stream = null;
        let rafHandle = null;
        let inFlight = false;
        let lastAt = 0;
        let frameNo = 0;

        async function tick() {
            if (!stream) return;
            const fpsTarget = Math.max(0.5, Number(getTargetFps?.() ?? 10));
            const interval = 1000 / fpsTarget;
            const now = performance.now();
            if (!inFlight && (now - lastAt) >= interval) {
                inFlight = true;
                lastAt = now;
                try {
                    frameNo += 1;
                    await onFrame({ frameNo, stream });
                } finally {
                    inFlight = false;
                }
            } else if (inFlight && onDroppedFrame) {
                onDroppedFrame();
            }
            rafHandle = requestAnimationFrame(tick);
        }

        async function start() {
            if (stream) return;
            const constraints = getConstraints?.() ?? { video: true, audio: false };
            stream = await navigator.mediaDevices.getUserMedia(constraints);
            if (videoElement) {
                videoElement.srcObject = stream;
            }
            rafHandle = requestAnimationFrame(tick);
        }

        async function stop() {
            if (rafHandle) {
                cancelAnimationFrame(rafHandle);
                rafHandle = null;
            }
            if (stream) {
                stream.getTracks().forEach(t => t.stop());
                stream = null;
            }
            inFlight = false;
            lastAt = 0;
        }

        return {
            start,
            stop,
            isRunning: () => !!stream,
            resetFrameCounter: () => { frameNo = 0; }
        };
    }

    function createDetectionTransport({
        getBaseUrl,
        getSessionId,
        onConnectionModeChanged,
        signalRGlobal = window.signalR
    }) {
        let hubConnection = null;
        let useWs = false;

        function baseUrl() {
            return String(getBaseUrl?.() ?? '').replace(/\/$/, '');
        }

        function sessionId() {
            return String(getSessionId?.() ?? '').trim();
        }

        async function connect() {
            if (!signalRGlobal || hubConnection) {
                if (onConnectionModeChanged) onConnectionModeChanged(useWs ? 'Connected' : 'HTTP');
                return;
            }

            hubConnection = new signalRGlobal.HubConnectionBuilder()
                .withUrl(`${baseUrl()}/detectionHub`)
                .withAutomaticReconnect()
                .build();
            try {
                await hubConnection.start();
                await hubConnection.invoke('JoinSession', sessionId());
                useWs = true;
                if (onConnectionModeChanged) onConnectionModeChanged('Connected');
            } catch {
                useWs = false;
                if (onConnectionModeChanged) onConnectionModeChanged('HTTP');
            }
        }

        async function initializeSession() {
            await fetch(`${baseUrl()}/api/detection/initialize/${encodeURIComponent(sessionId())}`, { method: 'POST' });
        }

        async function cleanupSession() {
            await fetch(`${baseUrl()}/api/detection/cleanup/${encodeURIComponent(sessionId())}`, { method: 'POST' });
        }

        async function processFrame(payload) {
            if (useWs && hubConnection) {
                try {
                    return await hubConnection.invoke('ProcessFrame', payload);
                } catch {
                    useWs = false;
                    if (onConnectionModeChanged) onConnectionModeChanged('HTTP');
                }
            }

            const response = await fetch(`${baseUrl()}/api/detection/process-frame`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(payload)
            });
            if (!response.ok) {
                return { __httpError: true, status: response.status, text: await response.text() };
            }
            return await response.json();
        }

        async function disconnect() {
            if (hubConnection) {
                try {
                    await hubConnection.invoke('LeaveSession', sessionId());
                    await hubConnection.stop();
                } catch {
                    // Ignore disconnect errors.
                }
                hubConnection = null;
            }
            useWs = false;
            if (onConnectionModeChanged) onConnectionModeChanged('—');
        }

        return {
            connect,
            disconnect,
            initializeSession,
            cleanupSession,
            processFrame,
            isWebSocketActive: () => !!useWs
        };
    }

    return {
        createToaster,
        setStatusDot,
        buildSceneOptions,
        bindProcessAll,
        createFrameProcessor,
        createDetectionTransport
    };
})();
