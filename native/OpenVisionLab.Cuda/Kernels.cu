// Each candidate keeps the CPU operation and accumulation order. Only candidates run in parallel.
extern "C" __global__ void edge_grid(const double* sourceX, const double* sourceY, int sourceCount,
    const int* offsets, const double* modelX, const double* modelY, const double* thresholds,
    int pointCount, int width, int minX, int minY, int columns, int step, int polarity,
    int first, int count, double* scores)
{
    int local = blockIdx.x * blockDim.x + threadIdx.x;
    if (local >= count) return;
    int candidate = first + local;
    long long center = (long long)(minY + (candidate / columns) * step) * width
        + minX + (candidate % columns) * step;
    double sum = 0.0;
    for (int i = 0; i < pointCount; ++i)
    {
        long long index = center + offsets[i];
        if (index < 0 || index >= sourceCount) { scores[local] = __longlong_as_double(0x7ff8000000000000LL); return; }
        double dot = __dadd_rn(__dmul_rn(sourceX[index], modelX[i]), __dmul_rn(sourceY[index], modelY[i]));
        sum = __dadd_rn(sum, dot);
        if (!polarity && sum < thresholds[i]) { scores[local] = __ddiv_rn(sum, (double)(i + 1)); return; }
    }
    scores[local] = pointCount == 0 ? 0.0 : __ddiv_rn(polarity ? fabs(sum) : sum, (double)pointCount);
}

extern "C" __global__ void transform_points(const double* points, int pointCount,
    const double* poses, int first, int count, double* transformed)
{
    int local = blockIdx.x * blockDim.x + threadIdx.x;
    if (local >= count) return;
    int item = first + local;
    const double* point = points + (item % pointCount) * 3;
    const double* pose = poses + (item / pointCount) * 12;
    for (int axis = 0; axis < 3; ++axis)
    {
        const double* row = pose + axis * 3;
        double value = __dadd_rn(__dmul_rn(row[0], point[0]), __dmul_rn(row[1], point[1]));
        value = __dadd_rn(value, __dmul_rn(row[2], point[2]));
        transformed[local * 3 + axis] = __dadd_rn(value, pose[9 + axis]);
    }
}

extern "C" __global__ void edge_grid_tile(const double* sourceX, const double* sourceY,
    const int* offsetX, const int* offsetY, const double* modelX, const double* modelY,
    const double* thresholds, int pointCount, int width, int height, int minX, int minY,
    int columns, int step, int polarity, int first, int count, double* scores)
{
    int local = blockIdx.x * blockDim.x + threadIdx.x;
    if (local >= count) return;
    int candidate = first + local;
    long long x = (long long)minX + (long long)(candidate % columns) * step;
    long long y = (long long)minY + (long long)(candidate / columns) * step;
    double sum = 0.0;
    for (int i = 0; i < pointCount; ++i)
    {
        long long sampleX = x + offsetX[i], sampleY = y + offsetY[i];
        if (sampleX < 0 || sampleX >= width || sampleY < 0 || sampleY >= height)
        { scores[local] = __longlong_as_double(0x7ff8000000000000LL); return; }
        long long index = sampleY * width + sampleX;
        double dot = __dadd_rn(__dmul_rn(sourceX[index], modelX[i]), __dmul_rn(sourceY[index], modelY[i]));
        sum = __dadd_rn(sum, dot);
        if (!polarity && sum < thresholds[i]) { scores[local] = __ddiv_rn(sum, (double)(i + 1)); return; }
    }
    scores[local] = pointCount == 0 ? 0.0 : __ddiv_rn(polarity ? fabs(sum) : sum, (double)pointCount);
}

__device__ double missing_height() { return __longlong_as_double(0x7ff8000000000000LL); }
extern "C" __global__ void median3d(const double* a,int w,int h,int k,int local,int minimum,double limit,int first,int batchCount,double* o)
{
    int localIndex=blockIdx.x*blockDim.x+threadIdx.x;if(localIndex>=batchCount)return;int i=first+localIndex;
    if(i>=w*h)return;
    double center=a[i];
    if(!isfinite(center)){o[i]=local?center:missing_height();return;}
    double b[49];int count=0,x=i%w,y=i/w,r=k/2;
    for(int v=max(0,y-r);v<=min(h-1,y+r);++v) for(int u=max(0,x-r);u<=min(w-1,x+r);++u)
    {
        if(local && v==y && u==x)continue;
        double item=a[v*w+u];if(!isfinite(item))continue;
        int j=count++;while(j>0 && b[j-1]>item){b[j]=b[j-1];--j;}b[j]=item;
    }
    if(local && count<minimum){o[i]=center;return;}
    double median=count%2?b[count/2]:(b[count/2-1]+b[count/2])/2.0;
    if(!isfinite(median))median=b[count/2-1]/2.0+b[count/2]/2.0;
    o[i]=local?(fabs(center-median)>limit?missing_height():center):median;
}
__device__ bool height_derivative(const double* a,int w,int h,int x,int y,int axis,double pitch,double* d,bool* central)
{
    int i=y*w+x,step=axis==0?1:w;
    bool low=(axis==0?x>0:y>0)&&isfinite(a[i-step]);
    bool high=(axis==0?x+1<w:y+1<h)&&isfinite(a[i+step]);
    *d=missing_height();*central=false;
    if(low&&high){*d=(a[i+step]-a[i-step])/(2.0*pitch);*central=true;}
    else if(high)*d=(a[i+step]-a[i])/pitch;
    else if(low)*d=(a[i]-a[i-step])/pitch;
    return isfinite(*d);
}
extern "C" __global__ void normals3d(const double* a,int w,int h,double cp,double rp,int first,int batchCount,double* o)
{
    int localIndex=blockIdx.x*blockDim.x+threadIdx.x;if(localIndex>=batchCount)return;int i=first+localIndex;if(i>=w*h)return;
    double *p=o+i*6;for(int j=0;j<5;++j)p[j]=missing_height();p[5]=0.0;
    if(!isfinite(a[i]))return;
    bool cx,cy;bool hx=height_derivative(a,w,h,i%w,i/w,0,cp,p,&cx);
    bool hy=height_derivative(a,w,h,i%w,i/w,1,rp,p+1,&cy);
    int flags=(hx?1:0)|(hy?2:0)|(cx?4:0)|(cy?8:0);
    if(hx&&hy)
    {
        double nx=-p[0],nz=-p[1],length=sqrt(nx*nx+1.0+nz*nz);
        if(isfinite(length)&&length>0.0){p[2]=nx/length;p[3]=1.0/length;p[4]=nz/length;flags|=16;}
    }
    p[5]=(double)flags;
}

// Fixed 8U Scharr contract: first X/Y derivative, scale 1, delta 0, Reflect101.
__device__ int reflect101(int position, int length)
{
    if (length == 1) return 0;
    if (position < 0) return -position;
    return position >= length ? 2 * length - position - 2 : position;
}
extern "C" __global__ void scharr8u(const unsigned char* input, int width, int height,
    int channels, int axis, int first, int count, unsigned char* output)
{
    int local = blockIdx.x * blockDim.x + threadIdx.x;
    if (local >= count) return;
    int index = first + local, channel = index % channels;
    int x = (index / channels) % width, y = index / (width * channels), sum = 0;
    for (int v = -1; v <= 1; ++v) for (int u = -1; u <= 1; ++u)
    {
        int sample = (reflect101(y + v, height) * width + reflect101(x + u, width)) * channels + channel;
        int coefficient = axis == 0 ? u * (v == 0 ? 10 : 3) : v * (u == 0 ? 10 : 3);
        sum += input[sample] * coefficient;
    }
    output[index] = (unsigned char)max(0, min(255, sum));
}
