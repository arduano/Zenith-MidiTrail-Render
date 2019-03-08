using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media;
using BMEngine;
using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;

namespace MidiTrailRender
{
    public class Render : IPluginRender
    {
        public string Name => "MidiTrail+";
        public string Description => "Clone of the popular tool MidiTrail for black midi rendering. Added exclusive bonus features, and less buggy.";

        public bool Initialized { get; set; } = false;

        public ImageSource PreviewImage => null;

        #region Shaders
        string whiteKeyShaderVert = @"#version 330 core

layout(location=0) in vec3 in_position;
layout(location=1) in float in_brightness;
layout(location=2) in float blend_fac;

out vec4 v2f_color;

uniform mat4 MVP;
uniform vec4 coll;
uniform vec4 colr;

void main()
{
    gl_Position = MVP * vec4(in_position, 1);
    //v2f_color = vec4(in_brightness, in_brightness, in_brightness, 1);
    v2f_color = vec4((coll.xyz * blend_fac + colr.xyz * (1 - blend_fac)) * in_brightness, 1);
}
";
        string whiteKeyShaderFrag = @"#version 330 core

in vec4 v2f_color;
layout (location=0) out vec4 out_color;

void main()
{
    out_color = v2f_color;
}
";

        string blackKeyShaderVert = @"#version 330 core

layout(location=0) in vec3 in_position;
layout(location=1) in float in_brightness;
layout(location=2) in float blend_fac;

out vec4 v2f_color;

uniform mat4 MVP;
uniform vec4 coll;
uniform vec4 colr;

void main()
{
    gl_Position = MVP * vec4(in_position, 1);
    //v2f_color = vec4(in_brightness, in_brightness, in_brightness, 1);
    v2f_color = vec4(1 - in_brightness + (coll.xyz * blend_fac + colr.xyz * (1 - blend_fac)) * in_brightness, 1);
}
";
        string blackKeyShaderFrag = @"#version 330 core

in vec4 v2f_color;
layout (location=0) out vec4 out_color;

void main()
{
    out_color = v2f_color;
}
";

        int MakeShader(string vert, string frag)
        {
            int _vertexObj = GL.CreateShader(ShaderType.VertexShader);
            int _fragObj = GL.CreateShader(ShaderType.FragmentShader);
            int statusCode;
            string info;

            GL.ShaderSource(_vertexObj, vert);
            GL.CompileShader(_vertexObj);
            info = GL.GetShaderInfoLog(_vertexObj);
            GL.GetShader(_vertexObj, ShaderParameter.CompileStatus, out statusCode);
            if (statusCode != 1) throw new ApplicationException(info);

            GL.ShaderSource(_fragObj, frag);
            GL.CompileShader(_fragObj);
            info = GL.GetShaderInfoLog(_fragObj);
            GL.GetShader(_fragObj, ShaderParameter.CompileStatus, out statusCode);
            if (statusCode != 1) throw new ApplicationException(info);

            int shader = GL.CreateProgram();
            GL.AttachShader(shader, _fragObj);
            GL.AttachShader(shader, _vertexObj);
            GL.LinkProgram(shader);
            return shader;
        }
        #endregion

        int whiteKeyShader;
        int blackKeyShader;
        int noteShader;

        int uWhiteKeyMVP;
        int uWhiteKeycoll;
        int uWhiteKeycolr;

        int uBlackKeyMVP;
        int uBlackKeycoll;
        int uBlackKeycolr;

        public bool ManualNoteDelete => false;

        public double LastMidiTimePerTick { get; set; } = 500000 / 96.0;

        public double NoteScreenTime => 600;

        public long LastNoteCount { get; private set; }

        public Control SettingsControl => null;

        bool[] blackKeys = new bool[257];
        int[] keynum = new int[257];

        RenderSettings renderSettings;
        Settings settings;

        int buffer3dtex;
        int buffer3dbuf;

        int whiteKeyVert;
        int whiteKeyCol;
        int whiteKeyIndx;
        int blackKeyVert;
        int blackKeyCol;
        int blackKeyIndx;
        int whiteKeyBlend;
        int blackKeyBlend;

        public void Dispose()
        {
            GL.DeleteBuffers(4, new int[] { whiteKeyVert, whiteKeyCol, blackKeyVert, blackKeyCol, whiteKeyIndx, blackKeyIndx });
            util.Dispose();
            Initialized = false;
            Console.WriteLine("Disposed of MidiTrailRender");
        }

        Util util;
        public Render(RenderSettings settings)
        {
            this.settings = new Settings();
            this.renderSettings = settings;
            //SettingsControl = new SettingsCtrl(this.settings);
            //PreviewImage = BitmapToImageSource(Properties.Resources.preview);
            for (int i = 0; i < blackKeys.Length; i++) blackKeys[i] = isBlackNote(i);
            int b = 0;
            int w = 0;
            for (int i = 0; i < keynum.Length; i++)
            {
                if (blackKeys[i]) keynum[i] = b++;
                else keynum[i] = w++;
            }
        }

        public void Init()
        {
            whiteKeyShader = MakeShader(whiteKeyShaderVert, whiteKeyShaderFrag);
            blackKeyShader = MakeShader(blackKeyShaderVert, blackKeyShaderFrag);

            uWhiteKeyMVP = GL.GetUniformLocation(whiteKeyShader, "MVP");
            uWhiteKeycoll = GL.GetUniformLocation(whiteKeyShader, "coll");
            uWhiteKeycolr = GL.GetUniformLocation(whiteKeyShader, "colr");

            uBlackKeyMVP = GL.GetUniformLocation(blackKeyShader, "MVP");
            uBlackKeycoll = GL.GetUniformLocation(blackKeyShader, "coll");
            uBlackKeycolr = GL.GetUniformLocation(blackKeyShader, "colr");

            GLUtils.GenFrameBufferTexture(renderSettings.width, renderSettings.height, out buffer3dbuf, out buffer3dtex, true);
            util = new Util();
            Initialized = true;
            Console.WriteLine("Initialised MidiTrailRender");


            whiteKeyVert = GL.GenBuffer();
            whiteKeyCol = GL.GenBuffer();
            blackKeyVert = GL.GenBuffer();
            blackKeyCol = GL.GenBuffer();
            whiteKeyIndx = GL.GenBuffer();
            blackKeyIndx = GL.GenBuffer();
            whiteKeyBlend = GL.GenBuffer();
            blackKeyBlend = GL.GenBuffer();
        }

        Color4[] keyColors = new Color4[514];
        double[] x1array = new double[257];
        double[] wdtharray = new double[257];
        public void RenderFrame(FastList<Note> notes, double midiTime, int finalCompositeBuff)
        {
            GL.Enable(EnableCap.Blend);
            GL.EnableClientState(ArrayCap.VertexArray);
            GL.EnableClientState(ArrayCap.ColorArray);
            GL.Enable(EnableCap.Texture2D);
            GL.Enable(EnableCap.DepthTest);

            GL.EnableVertexAttribArray(0);
            GL.EnableVertexAttribArray(1);
            GL.EnableVertexAttribArray(2);

            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, buffer3dbuf);
            GL.Viewport(0, 0, renderSettings.width, renderSettings.height);
            GL.Clear(ClearBufferMask.ColorBufferBit);
            GL.Clear(ClearBufferMask.DepthBufferBit);

            GL.UseProgram(whiteKeyShader);

            float whitekeylen = 5.5f;
            double[] verts = new double[] {
                //front
                0, 0, -whitekeylen,
                0, 0.7, -whitekeylen,
                1, 0.7, -whitekeylen,
                1, 0, -whitekeylen,
                //front dark
                0, 0.7, -whitekeylen,
                0, 1, -whitekeylen,
                1, 1, -whitekeylen,
                1, 0.7, -whitekeylen,
                //top
                1, 1, 0,
                1, 1, -whitekeylen,
                0, 1, -whitekeylen,
                0, 1, 0,
                //notch
                0, 1, -whitekeylen,
                0, 0.95, -whitekeylen - 0.1,
                1, 0.95, -whitekeylen - 0.1,
                1, 1, -whitekeylen,

                0, 0.85, -whitekeylen - 0.07,
                0, 0.95, -whitekeylen - 0.1,
                1, 0.95, -whitekeylen - 0.1,
                1, 0.85, -whitekeylen - 0.07,
                //left
                0, 1, 0,
                0, 1, -whitekeylen,
                0, 0, -whitekeylen,
                0, 0, 0,
                //right
                1, 1, 0,
                1, 1, -whitekeylen,
                1, 0, -whitekeylen,
                1, 0, 0,
            };

            float[] cols = new float[] {
                //front
                0.8f,
                0.8f,
                0.8f,
                0.8f,
                //front dark
                0.6f,
                0.6f,
                0.6f,
                0.6f,
                //top
                1,
                1,
                1,
                1,
                //notch
                1f,
                0.9f,
                0.9f,
                1f,

                0.9f,
                0.9f,
                0.9f,
                0.9f,
                //left
                0.6f,
                0.6f,
                0.6f,
                0.6f,
                //right
                0.6f,
                0.6f,
                0.6f,
                0.6f,
            };
            float[] blend = new float[] {
                //front
                1, 1, 1, 1,
                //front dark
                1, 1, 1, 1,
                //top
                0, 1, 1, 0,
                //notch
                1, 1, 1, 1,

                1, 1, 1, 1,
                //left
                0, 1, 1, 0,
                //right
                0, 1, 1, 0,
            };

            int[] indexes = new int[28];
            for (int i = 0; i < indexes.Length; i++) indexes[i] = i;

            GL.BindBuffer(BufferTarget.ArrayBuffer, whiteKeyVert);
            GL.BufferData(
                BufferTarget.ArrayBuffer,
                (IntPtr)(verts.Length * 8),
                verts,
                BufferUsageHint.StaticDraw);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Double, false, 24, 0);
            GL.BindBuffer(BufferTarget.ArrayBuffer, whiteKeyCol);
            GL.BufferData(
                BufferTarget.ArrayBuffer,
                (IntPtr)(cols.Length * 4),
                cols,
                BufferUsageHint.StaticDraw);
            GL.VertexAttribPointer(1, 1, VertexAttribPointerType.Float, false, 4, 0);
            GL.BindBuffer(BufferTarget.ArrayBuffer, whiteKeyBlend);
            GL.BufferData(
                BufferTarget.ArrayBuffer,
                (IntPtr)(blend.Length * 4),
                blend,
                BufferUsageHint.StaticDraw);
            GL.VertexAttribPointer(2, 1, VertexAttribPointerType.Float, false, 4, 0);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, whiteKeyIndx);
            GL.BufferData(
                BufferTarget.ElementArrayBuffer,
                (IntPtr)(indexes.Length * 4),
                indexes,
                BufferUsageHint.StaticDraw);
            GL.IndexPointer(IndexPointerType.Int, 1, 0);

            long nc = 0;
            int firstNote = settings.firstNote;
            int lastNote = settings.lastNote;
            bool sameWidth = settings.sameWidthNotes;
            float wdth;
            float wdth2;
            float r, g, b, a, r2, g2, b2, a2, r3, g3, b3, a3;
            float x1;
            float x2;
            double y1;
            double y2;
            Matrix4 mvp;
            double xx1, xx2, yy1, yy2;
            double ys1, ys2;
            if (settings.sameWidthNotes)
            {
                for (int i = 0; i < 257; i++)
                {
                    x1array[i] = (float)(i - firstNote) / (lastNote - firstNote);
                    wdtharray[i] = 1.0f / (lastNote - firstNote);
                }
            }
            else
            {
                double knmfn = keynum[firstNote];
                double knmln = keynum[lastNote - 1];
                if (blackKeys[firstNote]) knmfn = keynum[firstNote - 1] + 0.5;
                if (blackKeys[lastNote - 1]) knmln = keynum[lastNote] - 0.5;
                for (int i = 0; i < 257; i++)
                {
                    if (!blackKeys[i])
                    {
                        x1array[i] = (float)(keynum[i] - knmfn) / (knmln - knmfn + 1);
                        wdtharray[i] = 1.0f / (knmln - knmfn + 1);
                    }
                    else
                    {
                        int _i = i + 1;
                        wdth = (float)(0.6f / (knmln - knmfn + 1));
                        int bknum = keynum[i] % 5;
                        double offset = wdth / 2;
                        if (bknum == 0 || bknum == 2)
                        {
                            offset *= 1.3;
                        }
                        else if (bknum == 1 || bknum == 4)
                        {
                            offset *= 0.7;
                        }
                        x1array[i] = (float)(keynum[_i] - knmfn) / (knmln - knmfn + 1) - offset;
                        wdtharray[i] = wdth;
                    }
                }
            }

            #region Keyboard
            Color4[] origColors = new Color4[257];
            for (int k = firstNote; k < lastNote; k++)
            {
                if (isBlackNote(k))
                    origColors[k] = Color4.Black;
                else
                    origColors[k] = Color4.White;
            }

            for (int n = firstNote; n < lastNote; n++)
            {
                x1 = (float)x1array[n];
                wdth = (float)wdtharray[n];
                x2 = x1 + wdth;

                if (!blackKeys[n])
                {
                    y2 = 0;
                    if (settings.sameWidthNotes)
                    {
                        int _n = n % 12;
                        if (_n == 0)
                            x2 += wdth * 0.666f;
                        else if (_n == 2)
                        {
                            x1 -= wdth / 3;
                            x2 += wdth / 3;
                        }
                        else if (_n == 4)
                            x1 -= wdth / 3 * 2;
                        else if (_n == 5)
                            x2 += wdth * 0.75f;
                        else if (_n == 7)
                        {
                            x1 -= wdth / 4;
                            x2 += wdth / 2;
                        }
                        else if (_n == 9)
                        {
                            x1 -= wdth / 2;
                            x2 += wdth / 4;
                        }
                        else if (_n == 11)
                            x1 -= wdth * 0.75f;
                        wdth2 = wdth * 2;
                    }
                    else
                    {
                        wdth2 = wdth;
                    }
                }
                else continue;
                wdth = x2 - x1;
                x1 -= 0.5f;

                var coll = keyColors[n * 2];
                var colr = keyColors[n * 2 + 1];
                var origcol = origColors[n];
                float blendfac = coll.A;
                float revblendfac = 1 - blendfac;
                coll = new Color4(
                    coll.R * blendfac + origcol.R * revblendfac,
                    coll.G * blendfac + origcol.G * revblendfac,
                    coll.B * blendfac + origcol.B * revblendfac,
                    1);
                blendfac = colr.A;
                revblendfac = 1 - blendfac;
                colr = new Color4(
                    colr.R * blendfac + origcol.R * revblendfac,
                    colr.G * blendfac + origcol.G * revblendfac,
                    colr.B * blendfac + origcol.B * revblendfac,
                    1);

                GL.Uniform4(uWhiteKeycoll, coll);
                GL.Uniform4(uWhiteKeycolr, colr);

                mvp = Matrix4.Identity *
                    Matrix4.CreateScale(0.95f, 1, 1) *
                    Matrix4.CreateScale(wdth, wdth2, wdth2) *
                    Matrix4.CreateTranslation(x1, 0, 0) *
                    Matrix4.CreateTranslation(0, -0.5f, 0.4f) *
                    Matrix4.CreateScale(1, 1, -1) *
                    Matrix4.CreateRotationX(0.6f) *
                    Matrix4.CreatePerspectiveFieldOfView(3.1415f / 3, (float)renderSettings.width / (float)renderSettings.height, 0.01f, 400)
                    ;

                GL.UniformMatrix4(uWhiteKeyMVP, false, ref mvp);
                GL.DrawElements(PrimitiveType.Quads, indexes.Length, DrawElementsType.UnsignedInt, IntPtr.Zero);
            }

            GL.UseProgram(blackKeyShader);

            float blackkeylen = 7;
            verts = new double[] {
                //front
                0, 0, -blackkeylen,
                0, 1, -blackkeylen + 1,
                1, 1, -blackkeylen + 1,
                1, 0, -blackkeylen,
                //top
                0, 1, -blackkeylen + 1,
                0, 1, -0,
                1, 1, -0,
                1, 1, -blackkeylen + 1,
                //left
                0, 0, 0,
                0, 0, -blackkeylen,
                0, 1, -blackkeylen + 1,
                0, 1, 0,
                //left
                1, 0, 0,
                1, 0, -blackkeylen,
                1, 1, -blackkeylen + 1,
                1, 1, 0,
            };

            cols = new float[] {
                //front
                0.8f,
                0.9f,
                0.9f,
                0.8f,                
                //top
                1f,
                1f,
                1f,
                1f,              
                //left
                0.8f,
                0.8f,
                0.9f,
                0.8f,        
                //right
                0.8f,
                0.8f,
                0.9f,
                0.8f,
            };
            blend = new float[] {
                //front
                1, 1, 1, 1,
                //front
                1, 0, 0, 1,
                //left
                0, 1, 1, 0,
                //right
                0, 1, 1, 0,
            };

            indexes = new int[16];
            for (int i = 0; i < indexes.Length; i++) indexes[i] = i;

            GL.BindBuffer(BufferTarget.ArrayBuffer, blackKeyVert);
            GL.BufferData(
                BufferTarget.ArrayBuffer,
                (IntPtr)(verts.Length * 8),
                verts,
                BufferUsageHint.StaticDraw);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Double, false, 24, 0);
            GL.BindBuffer(BufferTarget.ArrayBuffer, blackKeyCol);
            GL.BufferData(
                BufferTarget.ArrayBuffer,
                (IntPtr)(cols.Length * 4),
                cols,
                BufferUsageHint.StaticDraw);
            GL.VertexAttribPointer(1, 1, VertexAttribPointerType.Float, false, 4, 0);
            GL.BindBuffer(BufferTarget.ArrayBuffer, blackKeyBlend);
            GL.BufferData(
                BufferTarget.ArrayBuffer,
                (IntPtr)(blend.Length * 4),
                blend,
                BufferUsageHint.StaticDraw);
            GL.VertexAttribPointer(2, 1, VertexAttribPointerType.Float, false, 4, 0);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, blackKeyIndx);
            GL.BufferData(
                BufferTarget.ElementArrayBuffer,
                (IntPtr)(indexes.Length * 4),
                indexes,
                BufferUsageHint.StaticDraw);
            GL.IndexPointer(IndexPointerType.Int, 1, 0);

            for (int n = firstNote; n < lastNote; n++)
            {
                x1 = (float)x1array[n];
                wdth = (float)wdtharray[n];
                x1 -= 0.5f;

                if (!blackKeys[n]) continue;

                var coll = keyColors[n * 2];
                var colr = keyColors[n * 2 + 1];
                var origcol = origColors[n];
                float blendfac = coll.A;
                float revblendfac = 1 - blendfac;
                coll = new Color4(
                    coll.R * blendfac + origcol.R * revblendfac,
                    coll.G * blendfac + origcol.G * revblendfac,
                    coll.B * blendfac + origcol.B * revblendfac,
                    1);
                blendfac = colr.A;
                revblendfac = 1 - blendfac;
                colr = new Color4(
                    colr.R * blendfac + origcol.R * revblendfac,
                    colr.G * blendfac + origcol.G * revblendfac,
                    colr.B * blendfac + origcol.B * revblendfac,
                    1);
                
                mvp = Matrix4.Identity *
                    Matrix4.CreateScale(0.95f, 1, 1) *
                    Matrix4.CreateTranslation(0, 2, 0) *
                    Matrix4.CreateScale(wdth, wdth, wdth) *
                    Matrix4.CreateTranslation(x1, 0, 0) *
                    Matrix4.CreateTranslation(0, -0.5f, 0.4f) *
                    Matrix4.CreateScale(1, 1, -1) *
                    Matrix4.CreateRotationX(0.6f) *
                    Matrix4.CreatePerspectiveFieldOfView(3.1415f / 3, (float)renderSettings.width / (float)renderSettings.height, 0.01f, 400)
                    ;

                GL.UniformMatrix4(uWhiteKeyMVP, false, ref mvp);
                GL.DrawElements(PrimitiveType.Quads, indexes.Length, DrawElementsType.UnsignedInt, IntPtr.Zero);
            }
            #endregion

            GL.Disable(EnableCap.Blend);
            GL.DisableClientState(ArrayCap.VertexArray);
            GL.DisableClientState(ArrayCap.ColorArray);
            GL.Disable(EnableCap.Texture2D);
            GL.Disable(EnableCap.DepthTest);

            GL.DisableVertexAttribArray(0);
            GL.DisableVertexAttribArray(1);
            GL.DisableVertexAttribArray(2);

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, finalCompositeBuff);
            GL.BindTexture(TextureTarget.Texture2D, buffer3dtex);
            GL.Clear(ClearBufferMask.ColorBufferBit);
            GL.Clear(ClearBufferMask.DepthBufferBit);
            util.DrawScreenQuad();
        }

        public void SetTrackColors(Color4[][] trakcs)
        {
            for (int i = 0; i < trakcs.Length; i++)
            {
                for (int j = 0; j < trakcs[i].Length / 2; j++)
                {
                    trakcs[i][j * 2] = Color4.FromHsv(new OpenTK.Vector4((i * 16 + j) * 1.36271f % 1, 1.0f, 1, 1f));
                    trakcs[i][j * 2 + 1] = Color4.FromHsv(new OpenTK.Vector4((i * 16 + j) * 1.36271f % 1, 1.0f, 1, 1f));
                }
            }
        }

        bool isBlackNote(int n)
        {
            n = n % 12;
            return n == 1 || n == 3 || n == 6 || n == 8 || n == 10;
        }
    }
}
