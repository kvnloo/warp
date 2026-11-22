// Only the Node API the vitest files use; avoids pulling @types/node into a browser app.
declare module "node:fs" {
  export function readFileSync(path: string | URL, encoding: "utf8"): string;
}
