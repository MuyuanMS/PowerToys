#pragma once
#include <queue>
#include <thread>
#include <mutex>
#include <condition_variable>
#include <chrono>
#include <string>

class AsyncMessageQueue
{
private:
    std::mutex queue_mutex;
    std::queue<std::wstring> message_queue;
    std::condition_variable message_ready;
    std::condition_variable idle;
    bool interrupted = false;
    bool processing = false;

    //Disable copy
    AsyncMessageQueue(const AsyncMessageQueue&);
    AsyncMessageQueue& operator=(const AsyncMessageQueue&);

public:
    AsyncMessageQueue()
    {
    }
    void queue_message(std::wstring message)
    {
        this->queue_mutex.lock();
        this->message_queue.push(message);
        this->queue_mutex.unlock();
        this->message_ready.notify_one();
    }
    std::wstring pop_message()
    {
        std::unique_lock<std::mutex> lock(this->queue_mutex);
        while (message_queue.empty() && !this->interrupted)
        {
            this->message_ready.wait(lock);
        }
        if (this->interrupted)
        {
            //Just returns an empty string if the queue was interrupted.
            return std::wstring(L"");
        }
        std::wstring message = this->message_queue.front();
        this->message_queue.pop();
        this->processing = true;
        return message;
    }
    bool wait_until_idle(std::chrono::milliseconds timeout)
    {
        std::unique_lock<std::mutex> lock(this->queue_mutex);
        return this->idle.wait_for(lock, timeout, [this] { return this->message_queue.empty() && !this->processing; });
    }
    void mark_message_processed()
    {
        std::lock_guard<std::mutex> lock(this->queue_mutex);
        this->processing = false;
        if (this->message_queue.empty())
        {
            this->idle.notify_all();
        }
    }
    void interrupt()
    {
        this->queue_mutex.lock();
        this->interrupted = true;
        this->queue_mutex.unlock();
        this->message_ready.notify_all();
    }
};
