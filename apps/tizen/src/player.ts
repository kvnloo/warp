export interface PlayerHandle {
  kind: "avplay" | "html5";
  play(url: string, startMs: number): Promise<void>;
  pause(): void;
  resume(): void;
  seekBy(deltaMs: number): void;
  timeMs(): number;
  stop(): void;
}

export function playerKind(): "avplay" | "html5" {
  return window.webapis?.avplay ? "avplay" : "html5";
}

export function createPlayer(
  video: HTMLVideoElement,
  onTime: (ms: number) => void,
  onFirstFrame: () => void,
  onError: (message: string) => void,
): PlayerHandle {
  const avplay = window.webapis?.avplay;
  if (!avplay) {
    let sawFrame = false;
    video.onplaying = () => {
      if (sawFrame) return;
      sawFrame = true;
      onFirstFrame();
    };
    video.ontimeupdate = () => onTime(Math.round(video.currentTime * 1000));
    video.onerror = () => onError(video.error?.message || "HTML5 video error");
    return {
      kind: "html5",
      play(url, startMs) {
        sawFrame = false;
        video.src = url;
        return video.play().then(() => {
          if (startMs > 0) video.currentTime = startMs / 1000;
        });
      },
      pause() { video.pause(); },
      resume() { void video.play(); },
      seekBy(deltaMs) {
        video.currentTime = Math.max(0, video.currentTime + deltaMs / 1000);
      },
      timeMs() { return Math.round(video.currentTime * 1000); },
      stop() {
        video.pause();
        video.removeAttribute("src");
        video.load();
      },
    };
  }

  let timeMs = 0;
  let sawFrame = false;
  return {
    kind: "avplay",
    play(url, startMs) {
      timeMs = startMs;
      sawFrame = false;
      avplay.close();
      avplay.open(url);
      avplay.setDisplayRect(0, 0, window.innerWidth || 1920, window.innerHeight || 1080);
      avplay.setDisplayMethod("PLAYER_DISPLAY_MODE_FULL_SCREEN");
      avplay.setListener({
        oncurrentplaytime(ms) { timeMs = ms; onTime(ms); },
        onbufferingcomplete() {
          if (sawFrame) return;
          sawFrame = true;
          onFirstFrame();
        },
        onerror(error) { onError(String(error)); },
      });
      return new Promise((resolve, reject) => {
        avplay.prepareAsync(() => {
          avplay.play();
          if (startMs > 0) avplay.seekTo(startMs);
          resolve();
        }, () => {
          onError("AVPlay prepare failed");
          reject(new Error("AVPlay prepare failed"));
        });
      });
    },
    pause() { avplay.pause(); },
    resume() { avplay.play(); },
    seekBy(deltaMs) { avplay.seekTo(Math.max(0, timeMs + deltaMs)); },
    timeMs() { return timeMs; },
    stop() {
      avplay.stop();
      avplay.close();
    },
  };
}
