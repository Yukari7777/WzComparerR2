// app/wz-types.ts
// WZ Primitives. Basically Lua table + custom meta
// WCR2 raw dumps now emit metadata through WzJsonMetaKeys; the types below
// describe the normalized shape after a parser maps those raw fields.
const FileRef = 'string' as const // placeholder for external file reference
export type WzVector = [x: number, y:number]
export type WzConvex = WzVector[]
export type WzNull = {} // expose key

export const WzJsonMetaPrefix = '@' as const
export const WzJsonMetaKeys = {
  type: `${WzJsonMetaPrefix}type`,
  value: `${WzJsonMetaPrefix}value`,
  format: `${WzJsonMetaPrefix}format`,
  scale: `${WzJsonMetaPrefix}scale`,
  pages: `${WzJsonMetaPrefix}pages`,
  length: `${WzJsonMetaPrefix}length`,
  ms: `${WzJsonMetaPrefix}ms`,
  channels: `${WzJsonMetaPrefix}channels`,
  frequency: `${WzJsonMetaPrefix}frequency`,
  points: `${WzJsonMetaPrefix}points`,
  data: `${WzJsonMetaPrefix}data`,
} as const

export type WzUol = {
  type: 'uol'
  value: string
}

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
  type: 'png'
  format?: keyof typeof WzTextureFormat
  scale?: number
  pages?: number

  //value will be parsed from its child node
  file?: typeof FileRef //_outlink should be parsed to this
}

// Additional normalized fields can be added here if the parser materializes
// more WCR2 metadata.
export type WzSound = WzDir & {
  type: 'sound'
  file?: typeof FileRef
}

export type WzVideo = WzDir & {
  type: 'video'
  file?: typeof FileRef
}

export type WzRaw = WzDir & {
  type: 'raw'
  size: number
  file?: typeof FileRef
}
