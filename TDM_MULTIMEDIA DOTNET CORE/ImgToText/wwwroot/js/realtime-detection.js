
// wwwroot/js/realtime-detection.js
class RealTimeDetection {
    constructor() {
        this.video = document.getElementById('videoFeed');
        this.canvas = document.getElementById('processingCanvas');
        this.ctx = this.canvas.getContext('2d');
        this.isRunning = false;
        this.sessionId = this.generateSessionId();
        this.processingInterval = null;
        this.statsInterval = null;
        this.fpsInterval = null;
        this.notificationCount = 0;
        this.logCount = 0;
        this.activeDetections = new Set();
        this.processingRate = 33; // ~30 FPS default
        this.frameCount = 0;
        this.lastFpsUpdate = 0;
        this.currentFps = 0;
        this.sessionStartTime = Date.now();

        // Hand detection setup
        this.hands = null;
        this.handResults = null;
        this.mediaPipeLoaded = false;

        // Enhanced detection tracking
        this.faceExpressions = [];
        this.handGestures = [];
        this.eyeMovements = [];
        this.capturedTexts = [];
        this.vitalMetrics = {};
        this.activityHistory = [];
        this.cameraStability = 100;



        this.initializeEventListeners();
        this.initializeSession();
        this.initializeCharts();
        this.initializeMediaPipeHands(); // Initialize MediaPipe
        this.updateUIState(false);
        this.updateTime();
        this.loadMediaPipeHands(); // Load MediaPipe dynamically
        setInterval(() => this.updateTime(), 1000);
    }



    async initializeMediaPipeHands() {
        try {
            // Check if MediaPipe is already loaded
            if (typeof Hands === 'undefined') {
                await this.loadMediaPipeScripts();
            }

            await this.setupMediaPipeHands();

        } catch (error) {
            console.error('Failed to initialize MediaPipe Hands:', error);
            this.addLog('MediaPipe Hands initialization failed. Using fallback detection.', 'warning');
            this.setupFallbackHandDetection();
        }
    }

    async loadMediaPipeScripts() {
        return new Promise((resolve, reject) => {
            const scripts = [
                'https://cdn.jsdelivr.net/npm/@mediapipe/camera_utils/camera_utils.js',
                'https://cdn.jsdelivr.net/npm/@mediapipe/drawing_utils/drawing_utils.js',
                'https://cdn.jsdelivr.net/npm/@mediapipe/hands/hands.js'
            ];

            let loadedCount = 0;

            scripts.forEach(src => {
                const script = document.createElement('script');
                script.src = src;
                script.crossOrigin = 'anonymous';

                script.onload = () => {
                    loadedCount++;
                    if (loadedCount === scripts.length) {
                        this.mediaPipeLoaded = true;
                        resolve();
                    }
                };

                script.onerror = () => {
                    console.error(`Failed to load script: ${src}`);
                    loadedCount++;
                    if (loadedCount === scripts.length && !this.mediaPipeLoaded) {
                        reject(new Error('Failed to load MediaPipe scripts'));
                    }
                };

                document.head.appendChild(script);
            });

            // Timeout fallback
            setTimeout(() => {
                if (!this.mediaPipeLoaded) {
                    reject(new Error('MediaPipe loading timeout'));
                }
            }, 10000);
        });
    }

    async setupMediaPipeHands() {
        if (typeof Hands === 'undefined') {
            throw new Error('MediaPipe Hands not available');
        }

        this.hands = new Hands({
            locateFile: (file) => {
                return `https://cdn.jsdelivr.net/npm/@mediapipe/hands/${file}`;
            }
        });

        this.hands.setOptions({
            maxNumHands: 2,
            modelComplexity: 1,
            minDetectionConfidence: 0.5,
            minTrackingConfidence: 0.5
        });

        this.hands.onResults((results) => {
            this.handResults = results;
            this.processHandResults(results);
        });

        this.initializedHands = true;
        this.addLog('MediaPipe Hands initialized successfully', 'success');
    }

    setupFallbackHandDetection() {
        this.initializedHands = false;
        this.addLog('Using fallback hand detection (simulation mode)', 'info');

        // Fallback to basic hand detection using canvas analysis
        this.fallbackHandDetector = {
            detect: (canvas) => this.fallbackDetectHands(canvas)
        };
    }

    // Fallback hand detection using basic image processing
    fallbackDetectHands(canvas) {
        // This is a very basic simulation of hand detection
        // In a real scenario, you might use a different approach or library
        const hands = [];
        // Simulate occasional hand detection
        if (Math.random() > 0.5) {
            hands.push({
                landmarks: this.generateSimulatedLandmarks(),
                confidence: 0.5,
                type: 'Simulated Hand'
            });
        }
        return hands;
    }

    generateSimulatedLandmarks() {
        // Generate 21 simulated landmarks (MediaPipe Hands has 21 landmarks per hand)
        const landmarks = [];
        for (let i = 0; i < 21; i++) {
            landmarks.push({
                x: Math.random(),
                y: Math.random(),
                z: Math.random() * 0.1
            });
        }
        return landmarks;
    }
    async loadMediaPipeHands() {
        try {
            // Dynamically load MediaPipe Hands
            await this.loadScript('https://cdn.jsdelivr.net/npm/@mediapipe/camera_utils/camera_utils.js');
            await this.loadScript('https://cdn.jsdelivr.net/npm/@mediapipe/drawing_utils/drawing_utils.js');
            await this.loadScript('https://cdn.jsdelivr.net/npm/@mediapipe/hands/hands.js');

            this.initializeHands();
        } catch (error) {
            console.error('Failed to load MediaPipe:', error);
            this.addLog('MediaPipe not available, using basic detection', 'warning');
        }
    }

    loadScript(src) {
        return new Promise((resolve, reject) => {
            if (document.querySelector(`script[src="${src}"]`)) {
                resolve();
                return;
            }

            const script = document.createElement('script');
            script.src = src;
            script.onload = resolve;
            script.onerror = reject;
            document.head.appendChild(script);
        });
    }
    initializeHands() {
        if (typeof Hands === 'undefined') {
            throw new Error('MediaPipe Hands not loaded');
        }

        this.hands = new Hands({
            locateFile: (file) => {
                return `https://cdn.jsdelivr.net/npm/@mediapipe/hands/${file}`;
            }
        });

        this.hands.setOptions({
            maxNumHands: 2,
            modelComplexity: 1,
            minDetectionConfidence: 0.5,
            minTrackingConfidence: 0.5
        });

        this.hands.onResults((results) => {
            this.handResults = results;
            this.processHandResults(results);
        });

        this.mediaPipeLoaded = true;
        this.addLog('Hand gesture detection initialized', 'success');
    }

    processHandResults(results) {
        if (!results.multiHandLandmarks || !this.isRunning) return;

        const gestures = [];

        results.multiHandLandmarks.forEach((landmarks, index) => {
            const handedness = results.multiHandedness[index];
            const gesture = this.analyzeHandGesture(landmarks, handedness);
            gestures.push(gesture);
        });

        this.handGestures = gestures;
        this.updateGestureAnalysis(gestures);
    }
    // Add the hand gesture analysis methods from previous solution
    analyzeHandGesture(landmarks, handedness) {
        const gesture = {
            type: 'Unknown',
            confidence: 1.0,
            handedness: handedness?.label || 'Unknown',
            meaning: '',
            landmarks: landmarks
        };

        const fingerStates = this.getFingerStates(landmarks);

        if (this.isOpenHand(fingerStates)) {
            gesture.type = 'Open Hand';
            gesture.meaning = 'All fingers extended';
        } else if (this.isClosedFist(fingerStates)) {
            gesture.type = 'Closed Fist';
            gesture.meaning = 'All fingers curled';
        } else if (this.isThumbsUp(fingerStates, handedness)) {
            gesture.type = 'Thumbs Up';
            gesture.meaning = 'Approval or positive signal';
        } else if (this.isVictory(fingerStates)) {
            gesture.type = 'Victory';
            gesture.meaning = 'Peace or victory sign';
        } else if (this.isPointing(fingerStates)) {
            gesture.type = 'Pointing';
            gesture.meaning = 'Index finger extended';
        } else if (this.isOkSign(fingerStates)) {
            gesture.type = 'OK Sign';
            gesture.meaning = 'Thumb and index finger circle';
        }

        return gesture;
    }

    getFingerStates(landmarks) {
        const FINGER_INDICES = {
            thumb: [1, 2, 3, 4],
            index: [5, 6, 7, 8],
            middle: [9, 10, 11, 12],
            ring: [13, 14, 15, 16],
            pinky: [17, 18, 19, 20]
        };

        const states = {};

        Object.keys(FINGER_INDICES).forEach(finger => {
            const tipIndex = FINGER_INDICES[finger][3];
            const pipIndex = FINGER_INDICES[finger][2];

            const tip = landmarks[tipIndex];
            const pip = landmarks[pipIndex];

            states[finger] = tip.y < pip.y;
        });

        return states;
    }

    isOpenHand(fingerStates) {
        return Object.values(fingerStates).every(extended => extended);
    }

    isClosedFist(fingerStates) {
        return Object.values(fingerStates).every(extended => !extended);
    }

    isThumbsUp(fingerStates, handedness) {
        return fingerStates.thumb &&
            !fingerStates.index &&
            !fingerStates.middle &&
            !fingerStates.ring &&
            !fingerStates.pinky;
    }

    isVictory(fingerStates) {
        return !fingerStates.thumb &&
            fingerStates.index &&
            fingerStates.middle &&
            !fingerStates.ring &&
            !fingerStates.pinky;
    }

    isPointing(fingerStates) {
        return !fingerStates.thumb &&
            fingerStates.index &&
            !fingerStates.middle &&
            !fingerStates.ring &&
            !fingerStates.pinky;
    }

    isOkSign(fingerStates) {
        return fingerStates.thumb &&
            fingerStates.index &&
            !fingerStates.middle &&
            !fingerStates.ring &&
            !fingerStates.pinky;
    }

    isOpenHand(fingerStates) {
        return Object.values(fingerStates).every(extended => extended);
    }

    isClosedFist(fingerStates) {
        return Object.values(fingerStates).every(extended => !extended);
    }

    isThumbsUp(fingerStates, handedness) {
        return fingerStates.thumb &&
            !fingerStates.index &&
            !fingerStates.middle &&
            !fingerStates.ring &&
            !fingerStates.pinky;
    }

    isVictory(fingerStates) {
        return !fingerStates.thumb &&
            fingerStates.index &&
            fingerStates.middle &&
            !fingerStates.ring &&
            !fingerStates.pinky;
    }

    isPointing(fingerStates) {
        return !fingerStates.thumb &&
            fingerStates.index &&
            !fingerStates.middle &&
            !fingerStates.ring &&
            !fingerStates.pinky;
    }

    isOkSign(fingerStates) {
        return fingerStates.thumb &&
            fingerStates.index &&
            !fingerStates.middle &&
            !fingerStates.ring &&
            !fingerStates.pinky;
    }

    drawHandLandmarks() {
        if (!this.handResults || !this.handResults.multiHandLandmarks) return;

        this.handResults.multiHandLandmarks.forEach((landmarks) => {
            // Draw landmarks
            landmarks.forEach((landmark) => {
                const x = landmark.x * this.canvas.width;
                const y = landmark.y * this.canvas.height;

                this.ctx.beginPath();
                this.ctx.arc(x, y, 3, 0, 2 * Math.PI);
                this.ctx.fillStyle = '#FF0000';
                this.ctx.fill();
            });

            // Draw connections
            this.drawHandConnections(landmarks);
        });
    }


    drawHandConnections(landmarks) {
        const connections = [
            [0, 1], [1, 2], [2, 3], [3, 4],
            [0, 5], [5, 6], [6, 7], [7, 8],
            [0, 9], [9, 10], [10, 11], [11, 12],
            [0, 13], [13, 14], [14, 15], [15, 16],
            [0, 17], [17, 18], [18, 19], [19, 20],
            [5, 9], [9, 13], [13, 17]
        ];

        this.ctx.strokeStyle = '#00FF00';
        this.ctx.lineWidth = 2;

        connections.forEach(([start, end]) => {
            const startPoint = landmarks[start];
            const endPoint = landmarks[end];

            const startX = startPoint.x * this.canvas.width;
            const startY = startPoint.y * this.canvas.height;
            const endX = endPoint.x * this.canvas.width;
            const endY = endPoint.y * this.canvas.height;

            this.ctx.beginPath();
            this.ctx.moveTo(startX, startY);
            this.ctx.lineTo(endX, endY);
            this.ctx.stroke();
        });
    }

    generateSessionId() {
        return 'session_' + Date.now() + '_' + Math.random().toString(36).substr(2, 9);
    }

    async initializeSession() {
        try {
            const response = await fetch(`/api/detection/initialize/${this.sessionId}`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' }
            });

            if (response.ok) {
                this.addLog('Advanced AI session initialized successfully', 'success');
                this.updateSystemStatus('ready');
            } else {
                this.addLog('Failed to initialize session, running in demo mode', 'warning');
                this.updateSystemStatus('ready');
            }
        } catch (error) {
            this.addLog('Error initializing advanced session: ' + error.message + ' - Running in demo mode', 'warning');
            this.updateSystemStatus('ready');
        }
    }

    initializeEventListeners() {
        // Control buttons
        document.getElementById('startBtn').addEventListener('click', () => this.startDetection());
        document.getElementById('stopBtn').addEventListener('click', () => this.stopDetection());
        document.getElementById('exportBtn').addEventListener('click', () => this.exportData());
        document.getElementById('clearLogsBtn').addEventListener('click', () => this.clearLogs());

        // Frame rate control
        const fpsSlider = document.getElementById('fpsSlider');
        if (fpsSlider) {
            fpsSlider.addEventListener('input', (e) => {
                const fps = parseInt(e.target.value);
                document.getElementById('fpsValue').textContent = fps;
                this.updateProcessingRate(fps);
            });
        }

        // Mobile menu
        const mobileMenuBtn = document.getElementById('mobileMenuBtn');
        if (mobileMenuBtn) {
            mobileMenuBtn.addEventListener('click', () => {
                document.querySelector('.sidebar').classList.toggle('open');
            });
        }

        // Tab system
        this.initializeTabSystem();

        // Monitoring options
        this.initializeMonitoringOptions();
    }

    initializeTabSystem() {
        const tabBtns = document.querySelectorAll('.tab-btn');
        tabBtns.forEach(btn => {
            btn.addEventListener('click', () => {
                // Remove active class from all buttons and panes
                document.querySelectorAll('.tab-btn').forEach(b => b.classList.remove('active'));
                document.querySelectorAll('.tab-pane').forEach(p => p.classList.remove('active'));

                // Add active class to clicked button and corresponding pane
                btn.classList.add('active');
                const tabId = btn.getAttribute('data-tab') + '-tab';
                document.getElementById(tabId).classList.add('active');
            });
        });
    }

    initializeMonitoringOptions() {
        const optionCards = document.querySelectorAll('.option-card');
        optionCards.forEach(card => {
            const checkbox = card.querySelector('input[type="checkbox"]');
            card.addEventListener('click', (e) => {
                if (e.target !== checkbox) {
                    checkbox.checked = !checkbox.checked;
                }
                if (checkbox.checked) {
                    card.classList.add('active');
                } else {
                    card.classList.remove('active');
                }
                this.updateSettings();
            });
        });
    }

    initializeCharts() {
        // Initialize emotion chart
        const emotionCtx = document.getElementById('emotionChart');
        if (emotionCtx) {
            this.emotionChart = new Chart(emotionCtx, {
                type: 'doughnut',
                data: {
                    labels: ['Happy', 'Sad', 'Angry', 'Surprised', 'Neutral'],
                    datasets: [{
                        data: [20, 10, 5, 15, 50],
                        backgroundColor: [
                            '#10b981',
                            '#3b82f6',
                            '#ef4444',
                            '#f59e0b',
                            '#6b7280'
                        ]
                    }]
                },
                options: {
                    responsive: true,
                    plugins: {
                        legend: {
                            position: 'bottom',
                            labels: {
                                color: '#e2e8f0'
                            }
                        }
                    }
                }
            });
        }

        // Initialize activity timeline
        const activityCtx = document.getElementById('activityTimeline');
        if (activityCtx) {
            this.activityTimeline = new Chart(activityCtx, {
                type: 'line',
                data: {
                    labels: Array.from({ length: 20 }, (_, i) => i + 1),
                    datasets: [{
                        label: 'Movement Level',
                        data: Array.from({ length: 20 }, () => Math.random() * 100),
                        borderColor: '#6366f1',
                        backgroundColor: 'rgba(99, 102, 241, 0.1)',
                        tension: 0.4,
                        fill: true
                    }]
                },
                options: {
                    responsive: true,
                    scales: {
                        x: {
                            grid: {
                                color: 'rgba(255, 255, 255, 0.1)'
                            },
                            ticks: {
                                color: '#94a3b8'
                            }
                        },
                        y: {
                            grid: {
                                color: 'rgba(255, 255, 255, 0.1)'
                            },
                            ticks: {
                                color: '#94a3b8'
                            }
                        }
                    }
                }
            });
        }
    }

    updateProcessingRate(fps) {
        this.processingRate = Math.floor(1000 / fps);
        this.restartProcessingIfRunning();
    }

    restartProcessingIfRunning() {
        if (this.isRunning) {
            clearInterval(this.processingInterval);
            this.processingInterval = setInterval(() => this.processFrame(), this.processingRate);
            this.addLog(`Processing rate updated to ${Math.floor(1000 / this.processingRate)} FPS`, 'info');
        }
    }

    updateTime() {
        const now = new Date();
        const timeElement = document.getElementById('currentTime');
        if (timeElement) {
            timeElement.textContent = now.toLocaleTimeString('en-US', { hour12: false });
        }
    }

    //async startDetection() {
    //    try {
    //        this.updateSystemStatus('initializing');
    //        this.updateCameraStatus('connecting');

    //        const stream = await navigator.mediaDevices.getUserMedia({
    //            video: {
    //                width: { ideal: 1280 },
    //                height: { ideal: 720 },
    //                frameRate: { ideal: 30 }
    //            }
    //        });

    //        this.video.srcObject = stream;

    //        this.video.onloadeddata = () => {
    //            this.canvas.width = this.video.videoWidth;
    //            this.canvas.height = this.video.videoHeight;

    //            this.isRunning = true;
    //            this.updateUIState(true);
    //            this.updateSystemStatus('running');
    //            this.updateCameraStatus('connected');
    //            this.updateProcessingStatus('active');

    //            this.processingInterval = setInterval(() => this.processFrame(), this.processingRate);
    //            this.statsInterval = setInterval(() => this.updateStats(), 1000);
    //            this.fpsInterval = setInterval(() => this.updateFPS(), 1000);

    //            this.addNotification('Advanced AI Vision System Activated', 'success');
    //            this.addLog('High-resolution camera initialized - Starting advanced analysis', 'info');
    //        };

    //    } catch (error) {
    //        this.addNotification('Camera Access Denied: ' + error.message, 'error');
    //        this.addLog('Camera initialization failed: ' + error.message, 'error');
    //        this.updateSystemStatus('error');
    //        this.updateCameraStatus('error');

    //        // Start simulation mode if camera fails
    //        this.startSimulationMode();
    //    }
    //}

    async startDetection() {
        try {
            this.updateSystemStatus('initializing');
            this.updateCameraStatus('connecting');

            const stream = await navigator.mediaDevices.getUserMedia({
                video: {
                    width: { ideal: 1280 },
                    height: { ideal: 720 },
                    frameRate: { ideal: 30 }
                }
            });

            this.video.srcObject = stream;

            this.video.onloadeddata = () => {
                this.canvas.width = this.video.videoWidth;
                this.canvas.height = this.video.videoHeight;

                this.isRunning = true;
                this.updateUIState(true);
                this.updateSystemStatus('running');
                this.updateCameraStatus('connected');
                this.updateProcessingStatus('active');

                this.processingInterval = setInterval(() => this.processFrame(), this.processingRate);
                this.statsInterval = setInterval(() => this.updateStats(), 1000);
                this.fpsInterval = setInterval(() => this.updateFPS(), 1000);

                this.addNotification('Advanced AI Vision System Activated', 'success');
                this.addLog('High-resolution camera initialized - Starting advanced analysis', 'info');
            };

        } catch (error) {
            this.addNotification('Camera Access Denied: ' + error.message, 'error');
            this.addLog('Camera initialization failed: ' + error.message, 'error');
            this.updateSystemStatus('error');
            this.updateCameraStatus('error');

            // Start simulation mode if camera fails
            this.startSimulationMode();
        }
    }

    //startSimulationMode() {
    //    this.addLog('Starting simulation mode with sample data', 'warning');
    //    this.isRunning = true;
    //    this.updateUIState(true);
    //    this.updateSystemStatus('running');
    //    this.updateProcessingStatus('active');

    //    this.processingInterval = setInterval(() => this.processSimulatedFrame(), this.processingRate);
    //    this.statsInterval = setInterval(() => this.updateSimulatedStats(), 1000);
    //    this.fpsInterval = setInterval(() => this.updateFPS(), 1000);
    //}

    //async processFrame() {
    //    if (!this.isRunning || this.video.readyState !== this.video.HAVE_ENOUGH_DATA) {
    //        return;
    //    }

    //    try {
    //        this.frameCount++;

    //        // Draw video frame
    //        this.ctx.drawImage(this.video, 0, 0, this.canvas.width, this.canvas.height);

    //        // Process with MediaPipe Hands if initialized
    //        if (this.initializedHands && this.hands) {
    //            await this.hands.send({ image: this.video });
    //            this.drawHandLandmarks();
    //        } else if (this.fallbackHandDetector) {
    //            // Use fallback hand detection
    //            const hands = this.fallbackHandDetector.detect(this.canvas);
    //            this.processFallbackHandResults(hands);
    //        }



    //        // Update statistics with hand detection data
    //        const stats = {
    //            facesDetected: 0, // You can add face detection later
    //            eyesDetected: 0,
    //            handsDetected: this.handResults?.multiHandLandmarks?.length || 0,
    //            totalFramesProcessed: this.frameCount,
    //            currentMovementLevel: this.calculateMovementLevel(),
    //            movementDetected: this.handResults?.multiHandLandmarks?.length > 0,
    //            textDetected: false,
    //            expressionsDetected: false,
    //            gesturesDetected: this.handGestures.length > 0
    //        };

    //        const result = {
    //            stats: stats,
    //            handGestures: this.handGestures,
    //            vitalMetrics: this.estimateVitalMetrics(),
    //            capturedText: null
    //        };

    //        this.updateDisplay(result);

    //    } catch (error) {
    //        this.addLog('Frame processing error: ' + error.message, 'error');

    //        // Fall back to simulation if MediaPipe fails
    //        if (!this.simulationWarningShown) {
    //            this.addLog('MediaPipe processing failed, switching to simulation mode', 'warning');
    //            this.simulationWarningShown = true;
    //        }
    //        this.processSimulatedFrame();
    //    }
    //}

    async processFrame() {
        if (!this.isRunning || this.video.readyState !== this.video.HAVE_ENOUGH_DATA) {
            return;
        }

        try {
            this.frameCount++;

            // Draw video frame
            this.ctx.drawImage(this.video, 0, 0, this.canvas.width, this.canvas.height);

            // Process with MediaPipe Hands if loaded
            if (this.mediaPipeLoaded && this.hands) {
                await this.hands.send({ image: this.video });
                this.drawHandLandmarks();
            } else {
                // Fallback to simulated hand detection
                this.processSimulatedHands();
            }

            // Update statistics
            const stats = {
                facesDetected: 0,
                eyesDetected: 0,
                handsDetected: this.handResults?.multiHandLandmarks?.length || 0,
                totalFramesProcessed: this.frameCount,
                currentMovementLevel: this.calculateMovementLevel(),
                movementDetected: this.handResults?.multiHandLandmarks?.length > 0,
                textDetected: false,
                expressionsDetected: false,
                gesturesDetected: this.handGestures?.length > 0
            };

            const result = {
                stats: stats,
                handGestures: this.handGestures || [],
                vitalMetrics: this.estimateVitalMetrics(),
                capturedText: null
            };

            this.updateDisplay(result);

        } catch (error) {
            console.error('Frame processing error:', error);
            this.processSimulatedFrame();
        }
    }




    processFallbackHandResults(hands) {
        const gestures = [];

        hands.forEach((hand, index) => {
            const gesture = {
                type: hand.type || 'Unknown',
                confidence: hand.confidence || 0.5,
                handedness: 'Unknown',
                meaning: 'Fallback detection',
                landmarks: hand.landmarks
            };
            gestures.push(gesture);
        });

        this.handGestures = gestures;
        this.updateGestureAnalysis(gestures);
    }

    processSimulatedHands() {
        // Basic simulation when MediaPipe is not available
        if (Math.random() > 0.7) {
            this.handGestures = [{
                type: ['Open Hand', 'Closed Fist', 'Thumbs Up'][Math.floor(Math.random() * 3)],
                confidence: 0.5 + Math.random() * 0.5,
                handedness: ['Left', 'Right'][Math.floor(Math.random() * 2)],
                meaning: 'Simulated gesture'
            }];
        } else {
            this.handGestures = [];
        }
    }

    calculateMovementLevel() {
        if (!this.handResults?.multiHandLandmarks) return Math.random() * 30;

        let totalMovement = 0;
        this.handResults.multiHandLandmarks.forEach(landmarks => {
            landmarks.forEach(landmark => {
                totalMovement += Math.sqrt(landmark.x ** 2 + landmark.y ** 2);
            });
        });

        return Math.min(100, totalMovement * 10);
    }
    estimateVitalMetrics() {
        const movementLevel = this.calculateMovementLevel();

        return {
            heartRate: Math.floor(60 + movementLevel / 3),
            stressLevel: movementLevel > 70 ? 'High' : movementLevel > 40 ? 'Medium' : 'Low',
            attentionScore: Math.floor(30 + movementLevel / 1.5),
            engagementLevel: movementLevel > 60 ? 'High' : movementLevel > 30 ? 'Medium' : 'Low'
        };
    }

    processSimulatedFrame() {
        if (!this.isRunning) return;

        this.frameCount++;

        // Simulate detection data with safe defaults
        const simulatedResult = {
            stats: {
                facesDetected: Math.floor(Math.random() * 3),
                eyesDetected: Math.floor(Math.random() * 6),
                handsDetected: Math.floor(Math.random() * 4),
                totalFramesProcessed: this.frameCount,
                currentMovementLevel: Math.random() * 100,
                movementDetected: Math.random() > 0.3,
                textDetected: Math.random() > 0.8,
                expressionsDetected: Math.random() > 0.5,
                gesturesDetected: Math.random() > 0.6
            },
            faceExpressions: Math.random() > 0.3 ? [{
                dominantEmotion: ['Happy', 'Sad', 'Angry', 'Surprised', 'Neutral'][Math.floor(Math.random() * 5)],
                emotions: {
                    happy: Math.random(),
                    sad: Math.random(),
                    angry: Math.random(),
                    surprised: Math.random(),
                    neutral: Math.random(),
                    fear: Math.random(),
                    disgust: Math.random()
                }
            }] : [],
            handGestures: Math.random() > 0.5 ? [{
                type: ['Open', 'Closed', 'Pointing', 'Thumbs Up', 'Peace'][Math.floor(Math.random() * 5)],
                confidence: Math.random(),
                handedness: ['Left', 'Right'][Math.floor(Math.random() * 2)],
                meaning: 'Simulated gesture'
            }] : [],
            vitalMetrics: {
                heartRate: Math.floor(60 + Math.random() * 40),
                stressLevel: ['Low', 'Medium', 'High'][Math.floor(Math.random() * 3)],
                attentionScore: Math.floor(50 + Math.random() * 50),
                engagementLevel: ['Low', 'Medium', 'High'][Math.floor(Math.random() * 3)]
            },
            capturedText: Math.random() > 0.8 ? 'Sample detected text from simulation mode' : null
        };

        this.updateDisplay(simulatedResult);
        this.drawDetectionOverlays(simulatedResult);
    }

    //processSimulatedFrame() {
    //    if (!this.isRunning) return;

    //    this.frameCount++;

    //    // Simulate detection data
    //    const simulatedResult = {
    //        stats: {
    //            facesDetected: Math.floor(Math.random() * 3),
    //            eyesDetected: Math.floor(Math.random() * 6),
    //            handsDetected: Math.floor(Math.random() * 4),
    //            totalFramesProcessed: this.frameCount,
    //            currentMovementLevel: Math.random() * 100,
    //            movementDetected: Math.random() > 0.3,
    //            textDetected: Math.random() > 0.8,
    //            expressionsDetected: Math.random() > 0.5,
    //            gesturesDetected: Math.random() > 0.6
    //        },
    //        faceExpressions: Math.random() > 0.3 ? [{
    //            dominantEmotion: ['Happy', 'Sad', 'Angry', 'Surprised', 'Neutral'][Math.floor(Math.random() * 5)],
    //            emotions: {
    //                happy: Math.random(),
    //                sad: Math.random(),
    //                angry: Math.random(),
    //                surprised: Math.random(),
    //                neutral: Math.random(),
    //                fear: Math.random(),
    //                disgust: Math.random()
    //            }
    //        }] : [],
    //        handGestures: Math.random() > 0.5 ? [{
    //            type: ['Open', 'Closed', 'Pointing', 'Thumbs Up', 'Peace'][Math.floor(Math.random() * 5)],
    //            confidence: Math.random(),
    //            handedness: ['Left', 'Right'][Math.floor(Math.random() * 2)],
    //            meaning: 'Simulated gesture'
    //        }] : [],
    //        vitalMetrics: {
    //            heartRate: Math.floor(60 + Math.random() * 40),
    //            stressLevel: ['Low', 'Medium', 'High'][Math.floor(Math.random() * 3)],
    //            attentionScore: Math.floor(50 + Math.random() * 50),
    //            engagementLevel: ['Low', 'Medium', 'High'][Math.floor(Math.random() * 3)]
    //        },
    //        capturedText: Math.random() > 0.8 ? 'Sample detected text from simulation mode' : null
    //    };

    //    this.updateDisplay(simulatedResult);
    //    this.drawDetectionOverlays(simulatedResult);
    //}

    updateSimulatedStats() {
        const stats = {
            facesDetected: Math.floor(Math.random() * 3),
            eyesDetected: Math.floor(Math.random() * 6),
            handsDetected: Math.floor(Math.random() * 4),
            totalFramesProcessed: this.frameCount,
            currentMovementLevel: Math.random() * 100
        };
        this.updateStatistics(stats);
        this.updateSidebarStats(stats);
    }

    stopDetection() {
        this.isRunning = false;
        this.updateProcessingStatus('idle');

        if (this.processingInterval) {
            clearInterval(this.processingInterval);
            this.processingInterval = null;
        }

        if (this.statsInterval) {
            clearInterval(this.statsInterval);
            this.statsInterval = null;
        }

        if (this.fpsInterval) {
            clearInterval(this.fpsInterval);
            this.fpsInterval = null;
        }

        if (this.video.srcObject) {
            this.video.srcObject.getTracks().forEach(track => track.stop());
            this.video.srcObject = null;
        }

        this.updateUIState(false);
        this.updateSystemStatus('ready');
        this.updateCameraStatus('offline');
        this.addNotification('Advanced Analysis System Stopped', 'warning');
        this.addLog('Real-time processing stopped', 'info');

        // Clear active detections
        this.activeDetections.clear();
        this.updateActiveDetections();
        this.clearVideoOverlay();
    }

    //async processFrame() {
    //    if (!this.isRunning || this.video.readyState !== this.video.HAVE_ENOUGH_DATA) {
    //        return;
    //    }

    //    try {
    //        this.frameCount++;
    //        this.ctx.drawImage(this.video, 0, 0, this.canvas.width, this.canvas.height);
    //        const imageData = this.canvas.toDataURL('image/jpeg', 0.9);

    //        const frameData = {
    //            imageData: imageData,
    //            sessionId: this.sessionId,
    //            timestamp: Date.now(),
    //            frameNumber: this.frameCount
    //        };

    //        const response = await fetch('/api/detection/process-frame', {
    //            method: 'POST',
    //            headers: { 'Content-Type': 'application/json' },
    //            body: JSON.stringify(frameData)
    //        });

    //        if (response.ok) {
    //            const result = await response.json();
    //            this.updateDisplay(result);
    //            this.drawDetectionOverlays(result);
    //        } else {
    //            throw new Error('Server returned error: ' + response.status);
    //        }
    //    } catch (error) {
    //        // If backend is not available, fall back to simulation
    //        if (error.message.includes('Failed to fetch') || error.message.includes('Server returned error')) {
    //            if (!this.simulationWarningShown) {
    //                this.addLog('Backend not available, switching to simulation mode', 'warning');
    //                this.simulationWarningShown = true;
    //            }
    //            this.processSimulatedFrame();
    //        } else {
    //            this.addLog('Frame processing error: ' + error.message, 'error');
    //        }
    //    }
    //}


    async processFrame() {
        if (!this.isRunning || this.video.readyState !== this.video.HAVE_ENOUGH_DATA) {
            return;
        }

        try {
            this.frameCount++;
            this.ctx.drawImage(this.video, 0, 0, this.canvas.width, this.canvas.height);
            const imageData = this.canvas.toDataURL('image/jpeg', 0.9);

            const frameData = {
                imageData: imageData,
                sessionId: this.sessionId,
                timestamp: Date.now(),
                frameNumber: this.frameCount
            };

            const response = await fetch('/api/detection/process-frame', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(frameData)
            });

            if (response.ok) {
                const result = await response.json();
                this.updateDisplay(result);
                this.drawDetectionOverlays(result);
            } else {
                throw new Error('Server returned error: ' + response.status);
            }
        } catch (error) {
            // If backend is not available, fall back to simulation
            if (error.message.includes('Failed to fetch') || error.message.includes('Server returned error')) {
                if (!this.simulationWarningShown) {
                    this.addLog('Backend not available, switching to simulation mode', 'warning');
                    this.simulationWarningShown = true;
                }
                this.processSimulatedFrame();
            } else {
                this.addLog('Frame processing error: ' + error.message, 'error');
            }
        }
    }


    safeUpdateElement(id, value, defaultValue = '0') {
        const element = document.getElementById(id);
        if (element) {
            element.textContent = value !== undefined && value !== null ? value : defaultValue;
        }
    }

    safeUpdateElementContent(id, content) {
        const element = document.getElementById(id);
        if (element && content !== undefined && content !== null) {
            element.textContent = content;
        }
    }

    safeUpdateStyle(id, property, value) {
        const element = document.getElementById(id);
        if (element) {
            element.style[property] = value;
        }
    }

    updateDisplay(result) {
        if (!result) return;

        this.updateStatistics(result.stats || {});
        this.updateMovementMeter(result.stats?.currentMovementLevel || 0);
        this.updateActiveDetections();
        this.updateFPSDisplay();
        this.updateCameraStability(result.stats?.currentMovementLevel || 0);

        // Enhanced data processing
        if (result.faceExpressions) {
            this.updateExpressionAnalysis(result.faceExpressions);
        }

        if (result.handGestures) {
            this.updateGestureAnalysis(result.handGestures);
        }

        if (result.eyeMovements) {
            this.updateEyeMovementAnalysis(result.eyeMovements);
        }

        if (result.vitalMetrics) {
            this.updateVitalMetrics(result.vitalMetrics);
        }

        if (result.notifications && result.notifications.length > 0) {
            result.notifications.forEach(notification => this.addNotification(notification.message, notification.type));
        }

        if (result.logs && result.logs.length > 0) {
            result.logs.forEach(log => this.addLog(log.message, log.type));
        }

        if (result.capturedText) {
            this.updateCapturedText(result.capturedText);
        }

        // Update sidebar stats
        this.updateSidebarStats(result.stats || {});

        // Update activity timeline
        this.updateActivityTimeline(result.stats || {});
    }

    //updateCameraStability(movementLevel) {
    //    // Calculate camera stability based on movement (inverse relationship)
    //    const stability = Math.max(0, 100 - movementLevel * 0.8);
    //    this.cameraStability = stability;

    //    const stabilityValue = document.getElementById('stabilityValue');
    //    const stabilityMeter = document.getElementById('stabilityMeter');
    //    const cameraStatusLabel = document.getElementById('cameraStatusLabel');

    //    if (stabilityValue) stabilityValue.textContent = stability.toFixed(0) + '%';
    //    if (stabilityMeter) stabilityMeter.style.width = stability + '%';

    //    if (cameraStatusLabel) {
    //        if (stability > 80) {
    //            cameraStatusLabel.textContent = 'Camera is stable';
    //            cameraStatusLabel.className = 'text-success';
    //        } else if (stability > 60) {
    //            cameraStatusLabel.textContent = 'Camera is moderately stable';
    //            cameraStatusLabel.className = 'text-warning';
    //        } else {
    //            cameraStatusLabel.textContent = 'Camera is unstable';
    //            cameraStatusLabel.className = 'text-danger';
    //        }
    //    }
    //}

    updateCameraStability(movementLevel) {
        // Calculate camera stability based on movement (inverse relationship)
        const stability = Math.max(0, 100 - (movementLevel || 0) * 0.8);
        this.cameraStability = stability;

        this.safeUpdateElement('stabilityValue', stability.toFixed(0) + '%');
        this.safeUpdateStyle('stabilityMeter', 'width', stability + '%');

        const cameraStatusLabel = document.getElementById('cameraStatusLabel');
        if (cameraStatusLabel) {
            if (stability > 80) {
                cameraStatusLabel.textContent = 'Camera is stable';
                cameraStatusLabel.className = 'text-success';
            } else if (stability > 60) {
                cameraStatusLabel.textContent = 'Camera is moderately stable';
                cameraStatusLabel.className = 'text-warning';
            } else {
                cameraStatusLabel.textContent = 'Camera is unstable';
                cameraStatusLabel.className = 'text-danger';
            }
        }
    }

    drawDetectionOverlays(result) {
        const overlay = document.getElementById('videoOverlay');
        if (!overlay) return;

        overlay.innerHTML = '';

        if (result.detections) {
            // Draw face markers with expressions
            if (result.detections.faces) {
                result.detections.faces.forEach((face, index) => {
                    const marker = this.createDetectionMarker(face.bbox, 'face', `Face ${index + 1} - ${face.expression || 'Neutral'}`);
                    overlay.appendChild(marker);
                });
            }

            // Draw eye markers with movement
            if (result.detections.eyes) {
                result.detections.eyes.forEach((eye, index) => {
                    const marker = this.createDetectionMarker(eye.bbox, 'eye', `Eye - ${eye.state || 'Open'}`);
                    overlay.appendChild(marker);
                });
            }

            // Draw hand markers with gestures
            if (result.detections.hands) {
                result.detections.hands.forEach((hand, index) => {
                    const marker = this.createDetectionMarker(hand.bbox, 'hand', `Hand - ${hand.gesture || 'Unknown'}`);
                    overlay.appendChild(marker);
                });
            }

            // Draw text regions
            if (result.detections.textRegions) {
                result.detections.textRegions.forEach((text, index) => {
                    const marker = this.createDetectionMarker(text.bbox, 'text', `Text: ${text.content}`);
                    overlay.appendChild(marker);
                });
            }
        }
    }

    createDetectionMarker(bbox, type, label) {
        const marker = document.createElement('div');
        marker.className = `detection-marker ${type}-marker`;
        marker.style.left = `${bbox?.x || 0}px`;
        marker.style.top = `${bbox?.y || 15}px`;
        marker.style.width = `${bbox?.width || 150}px`;
        marker.style.height = `${bbox?.height || 150}px`;

        // Add label
        const labelElement = document.createElement('div');
        labelElement.className = 'position-absolute top-0 start-0 translate-middle-y px-2 py-1 rounded text-xs';
        labelElement.style.background = 'rgba(0, 0, 0, 0.8)';
        labelElement.style.color = 'white';
        labelElement.style.fontSize = '10px';
        labelElement.style.whiteSpace = 'nowrap';
        labelElement.textContent = label;

        marker.appendChild(labelElement);
        return marker;
    }

    clearVideoOverlay() {
        const overlay = document.getElementById('videoOverlay');
        if (overlay) overlay.innerHTML = '';
    }

    updateExpressionAnalysis(expressions) {
        const container = document.getElementById('expressionAnalysis');
        if (!container) return;

        if (!expressions || expressions.length === 0) {
            container.innerHTML = `
                <div class="text-center text-muted py-5">
                    <i class="fas fa-user fa-3x mb-3"></i>
                    <p>No face detected for expression analysis</p>
                </div>
            `;
            return;
        }

        let html = '';
        expressions.forEach((expression, index) => {
            html += `
                <div class="expression-card">
                    <div class="d-flex justify-content-between align-items-center mb-2">
                        <h6 class="text-white mb-0">Face ${index + 1}</h6>
                        <span class="badge bg-primary">${expression.dominantEmotion}</span>
                    </div>
                    ${Object.entries(expression.emotions).map(([emotion, confidence]) => `
                        <div class="mb-2">
                            <div class="d-flex justify-content-between align-items-center">
                                <span class="text-light text-capitalize">${emotion}</span>
                                <span class="text-warning">${(confidence * 100).toFixed(1)}%</span>
                            </div>
                            <div class="expression-bar">
                                <div class="expression-fill" style="width: ${confidence * 100}%; background: ${this.getEmotionColor(emotion)};"></div>
                            </div>
                        </div>
                    `).join('')}
                </div>
            `;
        });

        container.innerHTML = html;

        // Update emotion chart
        if (expressions.length > 0 && this.emotionChart) {
            this.updateEmotionChart(expressions[0].emotions);
        }
    }

    updateEmotionChart(emotions) {
        const emotionData = {
            'Happy': (emotions.happy || 0) * 100,
            'Sad': (emotions.sad || 0) * 100,
            'Angry': (emotions.angry || 0) * 100,
            'Surprised': (emotions.surprised || 0) * 100,
            'Neutral': (emotions.neutral || 0) * 100
        };

        this.emotionChart.data.datasets[0].data = Object.values(emotionData);
        this.emotionChart.update();
    }

    getEmotionColor(emotion) {
        const colors = {
            'happy': '#10b981',
            'sad': '#3b82f6',
            'angry': '#ef4444',
            'surprised': '#f59e0b',
            'neutral': '#6b7280',
            'fear': '#8b5cf6',
            'disgust': '#84cc16'
        };
        return colors[emotion] || '#6366f1';
    }

    updateGestureAnalysis(gestures) {
        const container = document.getElementById('gestureAnalysis');
        const historyContainer = document.getElementById('gestureHistory');

        if (!container) return;

        if (!gestures || gestures.length === 0) {
            container.innerHTML = `
                <div class="text-center text-muted py-5">
                    <i class="fas fa-hand fa-3x mb-3"></i>
                    <p>No hand gestures detected</p>
                </div>
            `;
            return;
        }

        let html = '';
        gestures.forEach((gesture, index) => {
            html += `
                <div class="gesture-card">
                    <div class="d-flex justify-content-between align-items-center mb-2">
                        <h6 class="text-white mb-0">Hand ${index + 1}</h6>
                        <span class="badge bg-warning">${gesture.type}</span>
                    </div>
                    <div class="text-light">
                        <div class="mb-1">Confidence: <span class="text-warning">${((gesture.confidence || 0) * 100).toFixed(1)}%</span></div>
                        <div class="mb-1">Handedness: <span class="text-info">${gesture.handedness}</span></div>
                        ${gesture.meaning ? `<div>Meaning: <span class="text-muted">${gesture.meaning}</span></div>` : ''}
                    </div>
                </div>
            `;

            // Add to gesture history
            if (historyContainer) {
                const timestamp = new Date().toLocaleTimeString();
                const historyItem = document.createElement('div');
                historyItem.className = 'log-entry';
                historyItem.innerHTML = `
                    <i class="fas fa-hand me-2 text-warning"></i>
                    <span class="text-muted">[${timestamp}]</span>
                    <span class="ms-2">${gesture.handedness} hand: ${gesture.type}</span>
                `;
                historyContainer.appendChild(historyItem);

                // Keep history manageable
                if (historyContainer.children.length > 20) {
                    historyContainer.removeChild(historyContainer.firstChild);
                }
            }
        });

        container.innerHTML = html;
    }

    updateEyeMovementAnalysis(eyeMovements) {
        if (eyeMovements && eyeMovements.length > 0) {
            this.addLog(`Eye movements detected: ${eyeMovements.length} points tracked`, 'info');
        }
    }

    updateVitalMetrics(metrics) {
        this.vitalMetrics = { ...this.vitalMetrics, ...metrics };

        // Update UI elements
        if (metrics.heartRate) {
            const element = document.getElementById('heartRate');
            if (element) element.textContent = `${metrics.heartRate} BPM`;
        }
        if (metrics.stressLevel) {
            const element = document.getElementById('stressLevel');
            if (element) {
                element.textContent = metrics.stressLevel;
                element.className = `metric-value ${this.getStressLevelClass(metrics.stressLevel)}`;
            }
        }
        if (metrics.attentionScore) {
            const element = document.getElementById('attentionScore');
            if (element) element.textContent = `${metrics.attentionScore}%`;
        }
        if (metrics.engagementLevel) {
            const element = document.getElementById('engagementLevel');
            if (element) element.textContent = metrics.engagementLevel;
        }
    }

    getStressLevelClass(level) {
        const levels = {
            'Low': 'text-success',
            'Medium': 'text-warning',
            'High': 'text-danger',
            'Very High': 'text-danger'
        };
        return levels[level] || 'text-muted';
    }

    updateActivityTimeline(stats) {
        if (!this.activityTimeline) return;

        const now = new Date().toLocaleTimeString('en-US', { hour12: false });

        // Add current movement level to timeline
        this.activityHistory.push({
            time: now,
            movement: stats.currentMovementLevel || 0
        });

        // Keep only last 20 data points
        if (this.activityHistory.length > 20) {
            this.activityHistory.shift();
        }

        // Update chart
        this.activityTimeline.data.labels = this.activityHistory.map(item => item.time.split(':').slice(1).join(':'));
        this.activityTimeline.data.datasets[0].data = this.activityHistory.map(item => item.movement);
        this.activityTimeline.update();
    }

    updateFPS() {
        this.currentFps = this.frameCount;
        this.frameCount = 0;
    }

    updateFPSDisplay() {
        const liveFps = document.getElementById('liveFps');
        const fps = document.getElementById('fps');

        if (liveFps) liveFps.textContent = `${this.currentFps} FPS`;
        if (fps) fps.textContent = this.currentFps.toFixed(1);
    }

    //updateStatistics(stats) {
    //    const faceCount = document.getElementById('faceCount');
    //    const eyeCount = document.getElementById('eyeCount');
    //    const handCount = document.getElementById('handCount');
    //    const totalFrames = document.getElementById('totalFrames');

    //    if (faceCount) faceCount.textContent = stats.facesDetected || 0;
    //    if (eyeCount) eyeCount.textContent = stats.eyesDetected || 0;
    //    if (handCount) handCount.textContent = stats.handsDetected || 0;
    //    if (totalFrames) totalFrames.textContent = (stats.totalFramesProcessed || 0).toLocaleString();

    //    // Update active detections
    //    this.activeDetections.clear();
    //    if ((stats.facesDetected || 0) > 0) this.activeDetections.add('face');
    //    if ((stats.eyesDetected || 0) > 0) this.activeDetections.add('eye');
    //    if ((stats.handsDetected || 0) > 0) this.activeDetections.add('hand');
    //    if (stats.movementDetected) this.activeDetections.add('movement');
    //    if (stats.textDetected) this.activeDetections.add('text');
    //    if (stats.expressionsDetected) this.activeDetections.add('expression');
    //    if (stats.gesturesDetected) this.activeDetections.add('gesture');
    //}

    updateStatistics(stats = {}) {
        this.safeUpdateElement('faceCount', stats.facesDetected);
        this.safeUpdateElement('eyeCount', stats.eyesDetected);
        this.safeUpdateElement('handCount', stats.handsDetected);
        this.safeUpdateElement('totalFrames', (stats.totalFramesProcessed || 0).toLocaleString());

        // Update active detections
        this.activeDetections.clear();
        if ((stats.facesDetected || 0) > 0) this.activeDetections.add('face');
        if ((stats.eyesDetected || 0) > 0) this.activeDetections.add('eye');
        if ((stats.handsDetected || 0) > 0) this.activeDetections.add('hand');
        if (stats.movementDetected) this.activeDetections.add('movement');
        if (stats.textDetected) this.activeDetections.add('text');
        if (stats.expressionsDetected) this.activeDetections.add('expression');
        if (stats.gesturesDetected) this.activeDetections.add('gesture');

        this.updateActiveDetections();
    }


    //updateSidebarStats(stats) {
    //    const sidebarFaces = document.getElementById('sidebarFaces');
    //    const sidebarEyes = document.getElementById('sidebarEyes');
    //    const sidebarHands = document.getElementById('sidebarHands');
    //    const sidebarMovement = document.getElementById('sidebarMovement');

    //    if (sidebarFaces) sidebarFaces.textContent = stats.facesDetected || 0;
    //    if (sidebarEyes) sidebarEyes.textContent = stats.eyesDetected || 0;
    //    if (sidebarHands) sidebarHands.textContent = stats.handsDetected || 0;
    //    if (sidebarMovement) sidebarMovement.textContent = (stats.currentMovementLevel || 0).toFixed(0) + '%';
    //}


    updateSidebarStats(stats = {}) {
        this.safeUpdateElement('sidebarFaces', stats.facesDetected);
        this.safeUpdateElement('sidebarEyes', stats.eyesDetected);
        this.safeUpdateElement('sidebarHands', stats.handsDetected);
        this.safeUpdateElement('sidebarMovement', (stats.currentMovementLevel || 0).toFixed(0) + '%');
    }

    //updateMovementMeter(movementLevel) {
    //    const movementLevelNormalized = Math.min(100, Math.max(0, movementLevel));
    //    const meter = document.getElementById('movementMeter');
    //    const valueDisplay = document.getElementById('movementValue');

    //    if (meter) meter.style.width = movementLevelNormalized + '%';
    //    if (valueDisplay) valueDisplay.textContent = movementLevelNormalized.toFixed(1) + '%';

    //    // Update color based on intensity
    //    if (meter) {
    //        if (movementLevelNormalized < 10) {
    //            meter.style.background = 'linear-gradient(90deg, #4cc9f0, #4895ef)';
    //        } else if (movementLevelNormalized < 30) {
    //            meter.style.background = 'linear-gradient(90deg, #4895ef, #4361ee)';
    //        } else if (movementLevelNormalized < 50) {
    //            meter.style.background = 'linear-gradient(90deg, #4361ee, #f8961e)';
    //        } else {
    //            meter.style.background = 'linear-gradient(90deg, #f8961e, #f72585)';
    //        }
    //    }
    //}

    updateMovementMeter(movementLevel) {
        const movementLevelNormalized = Math.min(100, Math.max(0, movementLevel || 0));

        this.safeUpdateStyle('movementMeter', 'width', movementLevelNormalized + '%');
        this.safeUpdateElement('movementValue', movementLevelNormalized.toFixed(1) + '%');

        // Update color based on intensity
        const meter = document.getElementById('movementMeter');
        if (meter) {
            if (movementLevelNormalized < 10) {
                meter.style.background = 'linear-gradient(90deg, #4cc9f0, #4895ef)';
            } else if (movementLevelNormalized < 30) {
                meter.style.background = 'linear-gradient(90deg, #4895ef, #4361ee)';
            } else if (movementLevelNormalized < 50) {
                meter.style.background = 'linear-gradient(90deg, #4361ee, #f8961e)';
            } else {
                meter.style.background = 'linear-gradient(90deg, #f8961e, #f72585)';
            }
        }
    }


    updateActiveDetections() {
        const container = document.getElementById('activeDetections');
        if (!container) return;

        container.innerHTML = '';

        const detectionTypes = {
            'face': { name: 'Faces', icon: 'user', class: 'face-badge' },
            'eye': { name: 'Eyes', icon: 'eye', class: 'eye-badge' },
            'hand': { name: 'Hands', icon: 'hand-paper', class: 'hand-badge' },
            'movement': { name: 'Movement', icon: 'running', class: 'movement-badge' },
            'text': { name: 'Text', icon: 'font', class: 'text-badge' },
            'expression': { name: 'Expressions', icon: 'smile', class: 'expression-badge' },
            'gesture': { name: 'Gestures', icon: 'hand-rock', class: 'gesture-badge' }
        };

        this.activeDetections.forEach(detection => {
            const type = detectionTypes[detection];
            if (type) {
                const badge = document.createElement('span');
                badge.className = `detection-badge ${type.class}`;
                badge.innerHTML = `<i class="fas fa-${type.icon} me-1"></i>${type.name}`;
                container.appendChild(badge);
            }
        });

        if (this.activeDetections.size === 0) {
            container.innerHTML = '<span class="text-muted">No active detections</span>';
        }
    }


    updateFPSDisplay() {
        this.safeUpdateElement('liveFps', this.currentFps + ' FPS');
        this.safeUpdateElement('fps', this.currentFps.toFixed(1));
    }
    updateCapturedText(text) {
        const container = document.getElementById('capturedTextContent');
        if (!container) return;

        const timestamp = new Date().toLocaleTimeString();

        container.innerHTML = `
            <div class="mb-2">
                <small class="text-muted">[${timestamp}]</small>
            </div>
            <div class="p-2 bg-dark rounded">
                <p class="mb-0">${this.escapeHtml(text)}</p>
            </div>
        `;
    }

    escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

    //async updateStats() {
    //    if (!this.isRunning) return;

    //    try {
    //        const response = await fetch(`/api/detection/stats/${this.sessionId}`);
    //        if (response.ok) {
    //            const stats = await response.json();
    //            this.updateStatistics(stats);
    //            this.updateSidebarStats(stats);
    //        }
    //    } catch (error) {
    //        // Silently fail for stats updates in simulation mode
    //    }
    //}

    async updateStats() {
        if (!this.isRunning) return;

        try {
            const response = await fetch(`/api/detection/stats/${this.sessionId}`);
            if (response.ok) {
                const stats = await response.json();
                this.updateStatistics(stats);
                this.updateSidebarStats(stats);
            }
        } catch (error) {
            // Only log errors that aren't related to simulation mode
            if (!error.message.includes('Failed to fetch')) {
                this.addLog('Stats update error: ' + error.message, 'error');
            }
            // In simulation mode, we don't need to show stats update errors
        }
    }

    async updateSettings() {
        const settings = {
            enableFaceDetection: document.getElementById('enableFaceDetection')?.checked || true,
            enableEyeDetection: document.getElementById('enableEyeDetection')?.checked || true,
            enableHandDetection: document.getElementById('enableHandDetection')?.checked || true,
            enableMovementDetection: document.getElementById('enableMovementDetection')?.checked || true,
            enableTextDetection: document.getElementById('enableTextDetection')?.checked || true,
            movementThreshold: parseInt(document.getElementById('sensitivityRange')?.value) || 50
        };

        try {
            await fetch(`/api/detection/settings/${this.sessionId}`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(settings)
            });
            this.addLog('Detection settings updated', 'info');
        } catch (error) {
            // Silently fail in simulation mode
        }
    }

    addNotification(message, type = 'info') {
        const notifications = document.getElementById('notifications');
        if (!notifications) return;

        const timestamp = new Date().toLocaleTimeString();

        const notification = document.createElement('div');
        notification.className = `notification-item alert alert-${type} alert-dismissible fade show`;
        notification.innerHTML = `
            <div class="d-flex justify-content-between align-items-start">
                <div>
                    <small class="text-muted">[${timestamp}]</small>
                    <span class="ms-2">${message}</span>
                </div>
                <button type="button" class="btn-close btn-close-white" data-bs-dismiss="alert"></button>
            </div>
        `;

        notifications.appendChild(notification);
        notifications.scrollTop = notifications.scrollHeight;

        this.notificationCount++;
        const notificationCount = document.getElementById('notificationCount');
        if (notificationCount) notificationCount.textContent = this.notificationCount;

        // Auto-remove after 8 seconds
        setTimeout(() => {
            if (notification.parentNode) {
                notification.remove();
                this.notificationCount = Math.max(0, this.notificationCount - 1);
                if (notificationCount) notificationCount.textContent = this.notificationCount;
            }
        }, 8000);
    }

    addLog(message, type = 'info') {
        const logs = document.getElementById('logs');
        if (!logs) return;

        const timestamp = new Date().toLocaleTimeString();
        const typeIcon = {
            'info': 'info-circle',
            'success': 'check-circle',
            'warning': 'exclamation-triangle',
            'error': 'times-circle'
        }[type] || 'info-circle';

        const log = document.createElement('div');
        log.className = 'log-entry';
        log.innerHTML = `
            <i class="fas fa-${typeIcon} text-${type} me-2"></i>
            <span class="text-muted">[${timestamp}]</span>
            <span class="ms-2">${message}</span>
        `;

        logs.appendChild(log);
        logs.scrollTop = logs.scrollHeight;

        this.logCount++;
        const logCount = document.getElementById('logCount');
        if (logCount) logCount.textContent = this.logCount;

        if (logs.children.length > 50) {
            logs.removeChild(logs.firstChild);
        }
    }

    updateUIState(isRunning) {
        const startBtn = document.getElementById('startBtn');
        const stopBtn = document.getElementById('stopBtn');

        if (startBtn) startBtn.disabled = isRunning;
        if (stopBtn) stopBtn.disabled = !isRunning;
    }

    updateSystemStatus(status) {
        const indicator = document.getElementById('systemStatus');
        const text = document.getElementById('systemStatusText');

        const statusConfig = {
            'ready': { class: 'status-online', text: 'READY' },
            'initializing': { class: 'status-processing', text: 'INITIALIZING' },
            'running': { class: 'status-online pulse', text: 'RUNNING' },
            'error': { class: 'status-offline', text: 'ERROR' }
        };

        const config = statusConfig[status] || { class: 'status-offline', text: 'OFFLINE' };
        if (indicator) indicator.className = `status-indicator ${config.class}`;
        if (text) text.textContent = config.text;
    }

    updateCameraStatus(status) {
        const indicator = document.getElementById('cameraStatus');
        const text = document.getElementById('cameraStatusText');

        const statusConfig = {
            'offline': { class: 'status-offline', text: 'OFFLINE' },
            'connecting': { class: 'status-processing', text: 'CONNECTING' },
            'connected': { class: 'status-online', text: 'CONNECTED' },
            'error': { class: 'status-offline', text: 'ERROR' }
        };

        const config = statusConfig[status] || { class: 'status-offline', text: 'OFFLINE' };
        if (indicator) indicator.className = `status-indicator ${config.class}`;
        if (text) text.textContent = config.text;
    }

    updateProcessingStatus(status) {
        const indicator = document.getElementById('processingStatus');
        const text = document.getElementById('processingStatusText');

        const statusConfig = {
            'idle': { class: 'status-offline', text: 'IDLE' },
            'active': { class: 'status-online pulse', text: 'ACTIVE' },
            'error': { class: 'status-offline', text: 'ERROR' }
        };

        const config = statusConfig[status] || { class: 'status-offline', text: 'IDLE' };
        if (indicator) indicator.className = `status-indicator ${config.class}`;
        if (text) text.textContent = config.text;
    }

    async cleanup() {
        this.stopDetection();
        try {
            await fetch(`/api/detection/cleanup/${this.sessionId}`, { method: 'POST' });
            this.addLog('Session cleanup completed', 'info');
        } catch (error) {
            console.error('Error cleaning up session:', error);
        }
    }

    async exportData() {
        const exportData = {
            sessionId: this.sessionId,
            timestamp: new Date().toISOString(),
            duration: Date.now() - this.sessionStartTime,
            detections: {
                faces: this.faceExpressions,
                hands: this.handGestures,
                eyes: this.eyeMovements,
                text: this.capturedTexts
            },
            vitalMetrics: this.vitalMetrics,
            activityHistory: this.activityHistory,
            cameraStability: this.cameraStability
        };

        const blob = new Blob([JSON.stringify(exportData, null, 2)], { type: 'application/json' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `ai-vision-analysis-${this.sessionId}.json`;
        a.click();
        URL.revokeObjectURL(url);

        this.addNotification('Analysis data exported successfully', 'success');
    }

    clearLogs() {
        const logsContainer = document.getElementById('logs');
        if (logsContainer) {
            logsContainer.innerHTML = '';
            this.logCount = 0;
            const logCount = document.getElementById('logCount');
            if (logCount) logCount.textContent = '0';
            this.addLog('Logs cleared', 'info');
        }
    }
}

// Enhanced initialization
document.addEventListener('DOMContentLoaded', function () {
    window.detectionSystem = new RealTimeDetection();

    window.addEventListener('beforeunload', function () {
        if (window.detectionSystem) {
            window.detectionSystem.cleanup();
        }
    });
});










