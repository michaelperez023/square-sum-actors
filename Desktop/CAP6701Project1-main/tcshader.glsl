#version 400

layout(vertices = 4) out;

in vec4 vColor[];
out vec4 tcColor[];

uniform float TessLevelOuter;

#define ID gl_InvocationID

void main() {
    gl_out[gl_InvocationID].gl_Position = gl_in[gl_InvocationID].gl_Position;
    tcColor[gl_InvocationID] = vColor[gl_InvocationID];

    gl_TessLevelOuter[0] = 1;
    gl_TessLevelOuter[1] = TessLevelOuter;
}
