interface AvPlay {
  open(url: string): void;
  close(): void;
  prepareAsync(success: () => void, error: () => void): void;
  play(): void;
  pause(): void;
  seekTo(ms: number, success?: () => void, error?: () => void): void;
  stop(): void;
  setDisplayRect(x: number, y: number, width: number, height: number): void;
  setDisplayMethod(method: string): void;
  setListener(listener: {
    onbufferingstart?: () => void;
    onbufferingcomplete?: () => void;
    oncurrentplaytime?: (ms: number) => void;
    onstreamcompleted?: () => void;
    onerror?: (error: unknown) => void;
  }): void;
}

interface WebApis {
  avplay: AvPlay;
}

interface TizenInputDevice {
  registerKey(name: string): void;
  getSupportedKeys(): { name: string }[];
}

interface TizenGlobal {
  tvinputdevice: TizenInputDevice;
}

declare global {
  interface Window {
    webapis?: WebApis;
    tizen?: TizenGlobal;
  }
}

export {};
