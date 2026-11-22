import { PlexClient, imageUrl, type Hub, type MediaItem } from "@warp/plex";
import type { Session } from "./session";

/** Everything the screens read. Plex in production; a deterministic fixture only in mock builds. */
export interface LibrarySource {
  hubs(): Promise<Hub[]>;
  sections(): Promise<{ movieSectionId?: string; showSectionId?: string }>;
  metadata(ratingKey: string): Promise<MediaItem>;
  sectionAll(sectionId: string, type: number): Promise<MediaItem[]>;
  children(ratingKey: string): Promise<MediaItem[]>;
  selectStreams(partId: string, choice: { audioStreamID?: string; subtitleStreamID?: string }): Promise<void>;
  image(path: string | undefined, width: number, height: number): string;
}

export function plexSource(client: PlexClient, session: Session): LibrarySource {
  const { serverUri, token } = session;
  return {
    hubs: () => client.hubs(serverUri, token),
    sections: () => client.sections(serverUri, token),
    metadata: (ratingKey) => client.metadata(serverUri, token, ratingKey),
    sectionAll: (sectionId, type) => client.sectionAll(serverUri, token, sectionId, type),
    children: (ratingKey) => client.children(serverUri, token, ratingKey),
    selectStreams: (partId, choice) => client.selectStreams(serverUri, token, partId, choice),
    image: (path, width, height) => imageUrl(serverUri, token, path, width, height),
  };
}
