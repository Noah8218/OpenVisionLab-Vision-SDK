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
