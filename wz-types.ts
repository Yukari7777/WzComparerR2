// app/wz-types.ts
// WZ Primitives. Basically Lua table + custom meta
const FileRef = 'string' as const // placeholder for external file reference
export type WzVector = [x: number, y:number]
export type WzConvex = WzVector[]
export type WzNull = {} // expose key
export interface WzDir {
  [k: string]: any
}

export const WzTextureFormat = {
  0: 'Unknown',
  1: 'ARGB4444',
  2: 'ARGB8888',
  257: 'ARGB1555',
  513: 'RGB565',
  1026: 'DXT3',
  2050: 'DXT5',
  2304: 'A8',
  2562: 'RGBA1010102',
  4097: 'DXT1',
  4098: 'BC7',
  4100: 'RGBA32Float',
} as const

export type WzPng = WzDir & {
  type: 'png',
  format: keyof typeof WzTextureFormat,
  file: typeof FileRef //parse _outlink to this
  
  width?: number,
  height?: number,
  scale?: number,

  // multipage
  pages?: number,
}

export type WzUol = WzDir & {
  type: 'uol',
  value: string
}

//TODO: add meta from WCR2 if needed
export type WzSound = WzDir & {
  type: 'sound',
  file: typeof FileRef
}

export type WzVideo = WzDir & {
  type: 'video',
  file: typeof FileRef
}

export type WzRaw = WzDir & {
  type: 'raw',
  file: typeof FileRef,
  size: number
}
