#include <cstdio>
#include <cstdint>
#include <cstdlib>
#include <vector>
#include "libde265/de265.h"

int main(int argc, char** argv)
{
    if (argc != 2) return 2;
    FILE* input = nullptr;
    if (fopen_s(&input, argv[1], "rb") || !input) return 3;
    fseek(input, 0, SEEK_END);
    long size = ftell(input);
    rewind(input);
    std::vector<uint8_t> bytes(size);
    bool read = fread(bytes.data(), 1, bytes.size(), input) == bytes.size();
    fclose(input);
    if (!read) return 4;

    auto* ctx = de265_new_decoder();
    if (!ctx) return 5;
    de265_start_worker_threads(ctx, 4);
    de265_error err = de265_push_data(ctx, bytes.data(), (int)bytes.size(), 0, nullptr);
    if (err != DE265_OK) return 6;
    de265_flush_data(ctx);
    int frames = 0;
    uint64_t checksum = 0;
    int depth = 0, width = 0, height = 0;
    for (int iteration = 0; iteration < 10000 && frames < 5; ++iteration) {
        int more = 0;
        err = de265_decode(ctx, &more);
        while (auto* picture = de265_get_next_picture(ctx)) {
            width = de265_get_image_width(picture, 0);
            height = de265_get_image_height(picture, 0);
            depth = de265_get_bits_per_pixel(picture, 0);
            int stride = 0;
            const uint8_t* plane = de265_get_image_plane(picture, 0, &stride);
            for (int y = 0; y < height; ++y)
                for (int x = 0; x < width * (depth > 8 ? 2 : 1); ++x)
                    checksum = checksum * 31 + plane[y * stride + x];
            ++frames;
        }
        if (!more && err != DE265_OK) break;
    }
    de265_free_decoder(ctx);
    printf("libde265 %s %s: frames=%d %dx%d depth=%d checksum=%llu\n",
        de265_get_version(), sizeof(void*) == 8 ? "x64" : "x86", frames,
        width, height, depth, (unsigned long long)checksum);
    return frames >= 5 && checksum != 0 ? 0 : 7;
}
