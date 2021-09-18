﻿using System;
using System.Collections;
using System.Collections.Generic;
using Sora.Entities.MessageSegment;
using Sora.Entities.MessageSegment.Segment;
using Sora.Enumeration;

namespace Sora.Entities
{
    /// <summary>
    /// 消息段
    /// </summary>
    public class MessageBody : IList<SoraSegment<BaseSegment>>
    {
        #region 私有字段

        private readonly List<SoraSegment<BaseSegment>> _message = new();

        #endregion

        #region 属性

        /// <summary>
        /// 消息段数量
        /// </summary>
        public int Count => _message.Count;

        /// <summary>
        /// 只读
        /// </summary>
        public bool IsReadOnly => false;

        #endregion

        #region 构造方法

        /// <summary>
        /// 构造消息段列表
        /// </summary>
        public MessageBody(List<SoraSegment<BaseSegment>> messages)
        {
            _message.Clear();
            _message.AddRange(messages);
        }

        /// <summary>
        /// 构造消息段列表
        /// </summary>
        public MessageBody()
        {
        }

        #endregion

        #region List方法

        /// <summary>
        /// 迭代器
        /// </summary>
        IEnumerator<SoraSegment<BaseSegment>> IEnumerable<SoraSegment<BaseSegment>>.GetEnumerator()
        {
            return _message.GetEnumerator();
        }

        /// <summary>
        /// 迭代器
        /// </summary>
        public IEnumerator GetEnumerator()
        {
            return _message.GetEnumerator();
        }

        /// <summary>
        /// 添加消息段
        /// </summary>
        /// <param name="item">消息段</param>
        public void Add(SoraSegment<BaseSegment> item)
        {
            _message.Add(item);
        }
        
        /// <summary>
        /// 添加消息段
        /// </summary>
        /// <param name="item">消息段</param>
        public void Add<T>(SoraSegment<T> item) where T : BaseSegment
        {
            Add(item as SoraSegment<BaseSegment>);
        }

        /// <summary>
        /// 添加纯文本消息
        /// </summary>
        /// <param name="text">纯文本消息段</param>
        public void Add(string text)
        {
            _message.Add(SegmentBuilder.TextToBase(text));
        }

        /// <summary>
        /// 清空消息段
        /// </summary>
        public void Clear()
        {
            _message.Clear();
        }

        /// <summary>
        /// 判断包含
        /// </summary>
        /// <param name="item">消息段</param>
        public bool Contains(SoraSegment<BaseSegment> item)
        {
            return _message.Contains(item);
        }

        /// <summary>
        /// 复制
        /// </summary>
        /// <param name="array">消息段数组</param>
        /// <param name="arrayIndex">索引</param>
        public void CopyTo(SoraSegment<BaseSegment>[] array, int arrayIndex)
        {
            _message.CopyTo(array, arrayIndex);
        }

        /// <summary>
        /// 删除消息段
        /// </summary>
        /// <param name="item">消息段</param>
        public bool Remove(SoraSegment<BaseSegment> item)
        {
            return _message.Remove(item);
        }

        /// <summary>
        /// 索引查找
        /// </summary>
        /// <param name="item">消息段</param>
        public int IndexOf(SoraSegment<BaseSegment> item)
        {
            return _message.IndexOf(item);
        }

        /// <summary>
        /// 插入消息段
        /// </summary>
        /// <param name="index">索引</param>
        /// <param name="item">消息段</param>
        public void Insert(int index, SoraSegment<BaseSegment> item)
        {
            _message.Insert(index, item);
        }

        /// <summary>
        /// 删除消息段
        /// </summary>
        /// <param name="index">索引</param>
        public void RemoveAt(int index)
        {
            _message.RemoveAt(index);
        }

        /// <summary>
        /// AddRange
        /// </summary>
        public void AddRange(IEnumerable<SoraSegment<BaseSegment>> segments)
        {
            _message.AddRange(segments);
        }

        /// <summary>
        /// 转普通列表
        /// </summary>
        public List<SoraSegment<BaseSegment>> ToList()
        {
            return _message;
        }

        #endregion

        #region 扩展方法

        /// <summary>
        /// <para>添加纯文本消息段</para>
        /// <para>消息段扩展</para>
        /// </summary>
        /// <param name="text">纯文本信息</param>
        public void AddText(string text) =>
            _message.Add(SegmentBuilder.TextToBase(text));

        #endregion

        #region 运算重载

        /// <summary>
        /// 索引器
        /// </summary>
        /// <param name="index">索引</param>
        /// <exception cref="ArgumentOutOfRangeException">索引超出范围</exception>
        /// <exception cref="NullReferenceException">读取到了空消息段</exception>
        public SoraSegment<BaseSegment> this[int index]
        {
            get
            {
                if (_message.Count == 0 || index > _message.Count - 1 || index < 0)
                    throw new ArgumentOutOfRangeException(nameof(index));
                return _message[index];
            }
            set
            {
                if (value.MessageType == SegmentType.Unknown || value.DataObject == null)
                    throw new NullReferenceException("message element is null");
                _message[index] = value;
            }
        }

        /// <summary>
        /// 运算重载
        /// </summary>
        public static MessageBody operator +(MessageBody message, string text)
        {
            message.Add(SegmentBuilder.TextToBase(text));
            return message;
        }

        #endregion

        #region 隐式转换

        /// <summary>
        /// 隐式类型转换
        /// </summary>
        public static implicit operator MessageBody(string text)
        {
            return new() { SegmentBuilder.TextToBase(text) };
        }

        #endregion
    }
}