// A small, dependency-free JSON Schema subset validator.
// Supports: type, required, properties, additionalProperties:false, enum,
// items, minItems, maxItems, minLength, maxLength, minimum, maximum,
// pattern, $ref to local #/$defs/*, oneOf/anyOf (first match wins), const.

export function validateAgainstSchema(schema, data, { rootSchema = schema, path = '$' } = {}) {
  const errors = [];
  walk(schema, data, path, rootSchema, errors);
  return errors;
}

function resolveRef(ref, root) {
  if (!ref.startsWith('#/')) throw new Error(`Unsupported $ref: ${ref}`);
  const parts = ref.slice(2).split('/');
  let node = root;
  for (const p of parts) node = node?.[p];
  if (!node) throw new Error(`Unresolved $ref: ${ref}`);
  return node;
}

function typeOf(value) {
  if (value === null) return 'null';
  if (Array.isArray(value)) return 'array';
  if (Number.isInteger(value)) return 'integer';
  return typeof value;
}

function typeMatches(expected, value) {
  const actual = typeOf(value);
  if (expected === 'number') return actual === 'number' || actual === 'integer';
  return expected === actual;
}

function walk(schema, data, path, root, errors) {
  if (schema.$ref) schema = { ...resolveRef(schema.$ref, root), ...omit(schema, '$ref') };

  if (schema.const !== undefined && data !== schema.const) {
    errors.push({ path, message: `must equal ${JSON.stringify(schema.const)}` });
    return;
  }
  if (schema.enum && !schema.enum.includes(data)) {
    errors.push({ path, message: `must be one of ${schema.enum.map((v) => JSON.stringify(v)).join(', ')}` });
    return;
  }
  if (schema.type) {
    const types = Array.isArray(schema.type) ? schema.type : [schema.type];
    if (!types.some((t) => typeMatches(t, data))) {
      errors.push({ path, message: `must be ${types.join(' | ')}, got ${typeOf(data)}` });
      return;
    }
  }
  if (schema.oneOf || schema.anyOf) {
    const options = schema.oneOf || schema.anyOf;
    const attempts = options.map((opt) => {
      const e = [];
      walk(opt, data, path, root, e);
      return e;
    });
    if (!attempts.some((e) => e.length === 0)) {
      const best = attempts.slice().sort((a, b) => a.length - b.length)[0];
      errors.push(...best);
      return;
    }
  }
  const t = typeOf(data);
  if (t === 'string') {
    if (schema.minLength !== undefined && data.length < schema.minLength) errors.push({ path, message: `must be at least ${schema.minLength} characters` });
    if (schema.maxLength !== undefined && data.length > schema.maxLength) errors.push({ path, message: `must be at most ${schema.maxLength} characters` });
    if (schema.pattern && !new RegExp(schema.pattern).test(data)) errors.push({ path, message: `must match ${schema.pattern}` });
  }
  if (t === 'number' || t === 'integer') {
    if (schema.minimum !== undefined && data < schema.minimum) errors.push({ path, message: `must be >= ${schema.minimum}` });
    if (schema.maximum !== undefined && data > schema.maximum) errors.push({ path, message: `must be <= ${schema.maximum}` });
  }
  if (t === 'array') {
    if (schema.minItems !== undefined && data.length < schema.minItems) errors.push({ path, message: `must contain at least ${schema.minItems} items` });
    if (schema.maxItems !== undefined && data.length > schema.maxItems) errors.push({ path, message: `must contain at most ${schema.maxItems} items` });
    if (schema.items) data.forEach((item, i) => walk(schema.items, item, `${path}[${i}]`, root, errors));
  }
  if (t === 'object') {
    for (const key of schema.required || []) {
      if (!(key in data)) errors.push({ path, message: `missing required field "${key}"` });
    }
    const props = schema.properties || {};
    for (const [key, value] of Object.entries(data)) {
      if (props[key]) walk(props[key], value, `${path}.${key}`, root, errors);
      else if (schema.additionalProperties === false) errors.push({ path: `${path}.${key}`, message: `unknown field "${key}"` });
    }
  }
}

function omit(obj, key) {
  const copy = { ...obj };
  delete copy[key];
  return copy;
}
