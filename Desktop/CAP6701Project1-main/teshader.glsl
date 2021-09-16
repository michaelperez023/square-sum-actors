#version 400

layout(isolines) in;

in vec4 tcColor[];
out vec4 teColor[];

uniform mat4 ModelMatrix;
uniform mat4 ViewMatrix;
uniform mat4 ProjectionMatrix;
uniform float Knots[8];

#define ID gl_InvocationID

void main() {
    // The tessellation u coordinate
    float u = gl_TessCoord.x;

    //teColor[gl_InvocationID] = tcColor[gl_InvocationID];

    // deboor approach
    const int deg = 3;
    const int order = deg + 1;
    vec3 cps[order];
    for (int i = 0; i < order; i++) {
        cps[i] = gl_in[i].gl_Position.xyz;
    }

    // We only evaluate u between knot 4 and 5
    const int leftKnot = 4;

    for (int level = deg - 1; level >= 0; level--) {
        for (int i = 0; i <= level; i++) {
            float knot_lo = Knots[leftKnot + i - (level + 1)];
            float knot_hi = Knots[leftKnot + i];
            float alpha = (leftKnot + u - knot_lo) / (knot_hi - knot_lo);
            cps[i] = (1 - alpha) * cps[i] + alpha * cps[i + 1];
        }
    }

    vec3 tePosition = cps[0];

    gl_Position = ProjectionMatrix * ViewMatrix * ModelMatrix * vec4(tePosition, 1.0f);
}